using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LegacyOrders
{
    [DataContract]
    public sealed class Database
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public List<Order> Orders { get; set; }
    }

    public sealed class Storage : IDisposable
    {
        private readonly FileStream lease;
        private readonly string databasePath;
        public string Root { get; private set; }

        public Storage(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
            lease = new FileStream(Path.Combine(Root, "orders.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            databasePath = Path.Combine(Root, "orders.json");
        }

        public List<Order> Load()
        {
            if (!File.Exists(databasePath)) return new List<Order>();
            using (FileStream stream = File.OpenRead(databasePath))
            {
                Database db = (Database)new DataContractJsonSerializer(typeof(Database)).ReadObject(stream);
                if (db == null || db.Version != 1 || db.Orders == null)
                    throw new InvalidDataException("Unsupported or damaged order database. No data has been reset.");
                ValidateOrders(db.Orders);
                return db.Orders;
            }
        }

        public void Save(List<Order> orders)
        {
            ValidateOrders(orders);
            string temporary = databasePath + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(Database)).WriteObject(stream, new Database { Version = 1, Orders = orders });
                    stream.Flush(true);
                }
                if (File.Exists(databasePath)) File.Replace(temporary, databasePath, databasePath + ".bak");
                else File.Move(temporary, databasePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void ValidateOrders(List<Order> orders)
        {
            foreach (Order order in orders)
            {
                if (order == null) throw new InvalidDataException("Database contains an empty order.");
                order.Validate();
            }
            if (orders.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != orders.Count ||
                orders.Select(o => o.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Count() != orders.Count)
                throw new InvalidDataException("Order identifiers and references must be unique.");
        }

        public string OrderDirectory(Order order)
        {
            Guid id;
            if (!Guid.TryParseExact(order.Id, "N", out id)) throw new InvalidDataException("Invalid order identifier.");
            string directory = Path.Combine(Root, "documents", order.Id);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static bool IsAttachmentName(string name)
        {
            if (String.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return false;
            Guid id;
            return Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out id) &&
                IsAllowedExtension(Path.GetExtension(name));
        }

        public static bool IsAllowedExtension(string extension)
        {
            return new[] { ".pdf", ".txt", ".csv", ".png", ".jpg", ".jpeg", ".docx", ".xlsx" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public Attachment CopyAttachment(Order order, string source)
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (!IsAllowedExtension(extension))
                throw new InvalidOperationException("Allowed attachments: PDF, TXT, CSV, PNG, JPG, DOCX and XLSX.");
            FileInfo info = new FileInfo(source);
            if (info.Length > 25 * 1024 * 1024) throw new InvalidOperationException("Attachments are limited to 25 MiB.");
            string directory = OrderDirectory(order);
            NativeMethods.RequireFreeSpace(directory, (ulong)info.Length);
            Attachment attachment = new Attachment {
                OriginalName = Path.GetFileName(source), StoredName = Guid.NewGuid().ToString("N") + extension
            };
            File.Copy(source, Path.Combine(directory, attachment.StoredName), false);
            return attachment;
        }

        public void Dispose() { lease.Dispose(); }
    }
}

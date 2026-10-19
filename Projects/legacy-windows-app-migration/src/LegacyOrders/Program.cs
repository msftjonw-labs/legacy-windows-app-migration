using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace LegacyOrders
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyOrders");
            string import = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--data-directory" && i + 1 < args.Length) root = Path.GetFullPath(args[++i]);
                    else if (args[i] == "--import" && i + 1 < args.Length) import = Path.GetFullPath(args[++i]);
                    else throw new ArgumentException("Usage: LegacyOrders.exe [--data-directory directory] [--import orders.csv]");
                }
                Directory.CreateDirectory(root);
                Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(root, "application.log")));
                Trace.AutoFlush = true;
                using (Storage storage = new Storage(root))
                using (MainForm form = new MainForm(storage))
                {
                    if (import != null)
                    {
                        string importPath = import;
                        form.Shown += delegate {
                            form.Run(delegate {
                                if (MessageBox.Show(form, "Import orders from:\n" + importPath + "?", "Import CSV",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                    form.Import(importPath);
                            });
                        };
                    }
                    Application.Run(form);
                }
            }
            catch (Exception exception)
            {
                ReportError(null, exception);
                Environment.ExitCode = 1;
            }
            finally { Trace.Close(); }
        }

        internal static void ReportError(IWin32Window owner, Exception exception)
        {
            Trace.TraceError(exception.ToString());
            MessageBox.Show(owner, exception.Message + "\n\n" + exception.GetType().Name +
                "\n\nIf pricing activation failed, run scripts\\Register-Integrations.ps1 first.",
                "Legacy Orders - operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

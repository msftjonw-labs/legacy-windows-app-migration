# Legacy Orders modernization baseline

A deliberately desktop-coupled, single-user enterprise order-management sample:
C# WinForms on .NET Framework 4.8, x86 COM pricing, Win32 filesystem interop,
Explorer/document integration, and optional desktop Office automation. This is
the **legacy starting point**, not the containerized target or a production ERP.

## Build and run

Requirements: Windows with .NET Framework 4.8 or later and Windows PowerShell
5.1 or PowerShell 7. No .NET SDK, NuGet packages, Office installation, or
administrator rights are required for the baseline.

From the repository directory:

```powershell
.\build.ps1 -Test
.\scripts\Register-Integrations.ps1
.\bin\LegacyOrders.exe
```

The build uses the Windows-provided .NET Framework C# compiler, targeting x86.
With Visual Studio and the .NET Framework 4.8 targeting pack, you can also open
[the application project](src/LegacyOrders/LegacyOrders.csproj).
Keep the executable, its configuration, and the pricing DLL together. If you
move the directory, rerun registration to update the COM DLL's absolute path.

## Try the workflow

1. Click **Import CSV** and select [the sample orders](samples/orders.csv).
   The import creates two drafts and is atomic: conflicting order references or
   invalid rows reject the entire batch, without modifying existing orders.
2. Open an order; edit the customer, quantities, prices, discounts, or tax.
   Click **Save draft**, then **Calculate (COM)**.
3. Attach a supporting PDF, TXT, CSV, PNG, JPG, DOCX, or XLSX document.
   Attachments are copied into application storage with generated filenames,
   not linked to their original location. Each is limited to 25 MiB.
4. Click **Approve**. Approval validates COM pricing and freezes business fields.
5. Click **Generate invoice**. An HTML invoice is saved in the order folder,
   the order becomes `Invoiced`, and the default browser opens it. Use the
   browser's print command to print or save to PDF.
6. Use **Open attachment**, **Reveal in Explorer**, or **Order folder** to
   exercise workstation and Windows Shell dependencies.
7. Optionally use **Excel export** or **Word invoice** with locally installed,
   COM-registered desktop Office. Missing Office is reported, not silently
   replaced. Word invoices require approval. These exports run on the UI
   thread deliberately, as a realistic legacy coupling.

For sample order `PO-1001`, expect subtotal **2400.00 USD**, tax **198.00 USD**,
and total **2598.00 USD**. Currency is fixed to USD. Discounted line totals and
tax round to two decimals, midpoint away from zero.

CSV schema:

```csv
OrderReference,Customer,Description,Quantity,UnitPrice,DiscountPercent,TaxPercent
PO-1001,Contoso,Industrial pump,2,1250.00,10,8.25
```

CSV uses UTF-8, decimal points, and no currency symbols or thousands separators.
Quoted commas, escaped quotes, CRLF/LF, and multiline descriptions are supported.
Rows sharing a reference become one order and must share customer and tax.
Quantities are 1-100000; unit prices 0-10000000; discount/tax percentages 0-100.

## Data and registration

Data lives in `%LOCALAPPDATA%\LegacyOrders`:

- `orders.json`: versioned order records, atomically replaced on save.
- `orders.json.bak`: previous successful database version, once a replacement
  has occurred. Recovery is manual; corrupt data is reported, never reset.
- `documents\<order-id>`: copied attachments and generated HTML invoice.
- `application.log`: operation errors, including diagnostic exceptions.

Only one process can open a given data directory. A separate test dataset:

```powershell
.\bin\LegacyOrders.exe --data-directory "$env:TEMP\LegacyOrdersDemo"
```

COM setup writes only the current user's **32-bit** registry view:
`HKCU\Software\Classes\CLSID\{8FDE9697-E5CD-4747-A0DC-67770582AC6C}`
and `HKCU\Software\Classes\LegacyOrders.PricingEngine`.
The app activates the component through COM, not a direct assembly reference.
The COM component is managed C# exposed through `IDispatch`, **not native C++**.
Actual native Windows calls are made through P/Invoke.

Optionally install an Explorer CSV context-menu entry:

```powershell
.\scripts\Register-Integrations.ps1 -ShellMenu
```

Right-click a CSV and choose **Import into Legacy Orders** (on Windows 11,
possibly under **Show more options**). The app asks for confirmation before
importing. Close an existing app instance first because the dataset is locked.
The script does not replace the default CSV file association.

Remove these integrations, without deleting your data:

```powershell
.\scripts\Register-Integrations.ps1 -Unregister -ShellMenu
```

Only open trusted attachments. This sample is not a document sandbox or malware
scanner. Office exports use new documents, not user-provided templates.

## Validation

```powershell
.\build.ps1 -Test
# After per-user COM registration:
.\bin\LegacyOrders.Tests.exe --com --ui
# Optional native IDispatch client check, if VBScript/Windows Script Host is enabled:
& "$env:WINDIR\SysWOW64\cscript.exe" //nologo .\tests\ComSmoke.vbs
```

The dependency-free test executable checks pricing bounds/rounding, CSV parsing,
atomic persistence, backups, duplicate rejection, exclusive locking, corrupt
data handling, attachment validation/copy/size limits, native disk-space calls,
and HTML encoding/output replacement. Optional checks exercise actual x86 COM
activation and show/close WinForms windows while testing import, editing, save,
displayed totals, and approved-order read-only behavior.

Manual checks still required: approval confirmation, browser printing, attachment
application launch, Explorer menu/reveal, and Word/Excel automation with Office.
GUI tests require an interactive Windows session.

## Modernization seams

See the [modernization plan](docs/modernization-plan.md) for the .NET 10,
Windows ACI backend, and Azure Static Web Apps frontend migration path.

| Legacy dependency | Current location | Intended modernization |
|---|---|---|
| UI-driven order workflow and status transitions | `MainForm.cs` | Extract application service and HTTP API; preserve draft/approved/invoiced rules |
| Registered x86 COM pricing | `Domain.cs`, `PricingEngine.cs` | Evaluate headless container compatibility or replace behind a pricing interface |
| Local JSON state and process lock | `Storage.cs` | External database, transactions, concurrent-access rules |
| Local document directories | `Storage.cs`, `Documents.cs` | External document storage and authorized downloads |
| `GetDiskFreeSpaceEx` P/Invoke | `NativeMethods.cs` | Reevaluate against backend storage architecture |
| Shell execution / Explorer / CSV verb | `NativeMethods.cs`, registration script | Keep in a Windows client/companion or redesign for browser capabilities |
| Word/Excel COM automation | `OfficeExport.cs` | Replace with headless document libraries or service APIs |

No frontend split, backend service, Windows container, MSI installer, user
authentication, multi-user authorization, audit ledger, or production-grade
accounting is implemented yet. Desktop UI, Explorer, and Office automation
must not simply be moved into a Windows container.

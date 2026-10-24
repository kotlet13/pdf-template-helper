using System.Diagnostics;
using System.Text.Json;
using PdfTemplateHelper;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new HelperWindow());
    }
}

sealed class HelperWindow : Form
{
    readonly TextBox template = new() { Dock = DockStyle.Fill, ReadOnly = true };
    readonly TextBox request = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsTab = true, Font = new Font("Consolas", 10), Text = "{\r\n  \"fields\": {},\r\n  \"checkboxes\": {},\r\n  \"flatten\": true,\r\n  \"strict\": true\r\n}" };
    readonly TextBox status = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Label assetLabel = new() { AutoSize = true, Text = "Asset folder: choose the directory containing relative request assets." };
    readonly List<Button> actionButtons = [];
    readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    CancellationTokenSource? activeRun;
    bool closeWhenIdle;
    string? assetRoot;
    string? package;
    public HelperWindow()
    {
        Text = "PDF Template Helper"; Width = 1000; Height = 760; MinimumSize = new Size(760, 550);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.Controls.Add(Button("Choose template", ChooseTemplate), 0, 0); top.Controls.Add(template, 1, 0); top.Controls.Add(Button("Inspect fields", Inspect), 2, 0);
        layout.Controls.Add(top, 0, 0);
        var assets = new FlowLayoutPanel { Dock = DockStyle.Fill };
        assets.Controls.Add(Button("Asset folder", ChooseAssets)); assets.Controls.Add(assetLabel); layout.Controls.Add(assets, 0, 1);
        layout.Controls.Add(request, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(Button("Load request JSON", LoadJson)); actions.Controls.Add(Button("Open .pdfjob", OpenJob));
        actions.Controls.Add(Button("Use starter request", UseStarter));
        actions.Controls.Add(Button("Save .pdfjob", SaveJob)); actions.Controls.Add(AsyncButton("Fill and save PDF", Fill));
        cancelButton.Click += (_, _) => CancelRun(); actions.Controls.Add(cancelButton);
        layout.Controls.Add(actions, 0, 3); layout.Controls.Add(status, 0, 4); Controls.Add(layout);
        status.Text = "Choose a PDF template and edit the request JSON, or open a portable .pdfjob.\r\nCoordinates are PDF points, from the bottom-left. Processing runs locally.\r\nA packaged job includes every referenced image/font. iText licensing: AGPL or commercial.";
        FormClosing += (_, e) =>
        {
            if (activeRun is null) return;
            e.Cancel = true; closeWhenIdle = true; CancelRun();
        };
    }
    Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30 };
        button.Click += (_, _) => { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "PDF Template Helper", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        actionButtons.Add(button);
        return button;
    }
    Button AsyncButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30 };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception error) { MessageBox.Show(error.Message, "PDF Template Helper", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        actionButtons.Add(button); return button;
    }
    static string? Open(string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
    static string? Save(string filter, string extension)
    {
        using var dialog = new SaveFileDialog { Filter = filter, DefaultExt = extension, AddExtension = true };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
    void ChooseTemplate()
    {
        if (Open("PDF templates|*.pdf") is not { } file) return;
        // A package displays the outer manifest; manual mode edits only request.
        if (package is not null)
            request.Text = "{\r\n  \"fields\": {},\r\n  \"checkboxes\": {},\r\n  \"flatten\": true,\r\n  \"strict\": true\r\n}";
        template.Text = file; package = null; request.ReadOnly = false;
        assetRoot = Path.GetDirectoryName(file); assetLabel.Text = "Asset folder: " + assetRoot;
        Inspect();
    }
    void Inspect()
    {
        if (package is not null)
        {
            using var directory = JobService.Extract(package);
            var manifest = JsonSerializer.Deserialize<JobDocument>(File.ReadAllText(Path.Combine(directory.Path, "job.json")), JobService.Json)!;
            status.Text = JsonSerializer.Serialize(JobService.ListFields(JobService.Resolve(directory.Path, manifest.Template)), JobService.Json);
        }
        else status.Text = JsonSerializer.Serialize(JobService.ListFields(template.Text), JobService.Json);
    }
    void ChooseAssets()
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK) { assetRoot = dialog.SelectedPath; assetLabel.Text = "Asset folder: " + assetRoot; }
    }
    void LoadJson()
    {
        if (Open("Request JSON|*.json") is not { } file) return;
        request.Text = File.ReadAllText(file); assetRoot = Path.GetDirectoryName(file); assetLabel.Text = "Asset folder: " + assetRoot; package = null; request.ReadOnly = false;
    }
    void OpenJob()
    {
        if (Open("Portable PDF jobs|*.pdfjob") is not { } file) return;
        using var directory = JobService.Extract(file);
        var json = File.ReadAllText(JobService.Resolve(directory.Path, "job.json"));
        var manifest = JsonSerializer.Deserialize<JobDocument>(json, JobService.Json)!;
        JobService.Resolve(directory.Path, manifest.Template);
        package = file; template.Text = file; request.Text = json; request.ReadOnly = true;
        status.Text = "Package opened. Inspect fields or fill and save. Choose a template to return to manual editing.";
    }
    void BuildPackage(string target)
        => BuildPackage(target, package, template.Text, request.Text, assetRoot);
    static void BuildPackage(string target, string? packageSource, string templatePath, string requestText, string? assetsPath)
    {
        if (packageSource is not null)
        {
            if (!Path.GetFullPath(packageSource).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(packageSource, target, true);
            return;
        }
        using var temp = new TemporaryDirectory();
        var json = Path.Combine(temp.Path, "request.json"); File.WriteAllText(json, requestText);
        JobService.CreateJob(templatePath, target, json, assetsPath);
    }
    void UseStarter()
    {
        if (package is not null) throw new InvalidOperationException("Choose a PDF template before creating a manual starter request.");
        request.Text = JsonSerializer.Serialize(JobService.CreateStarterRequest(template.Text), JobService.Json);
        status.Text = "Starter request created from template fields. Edit its values before filling. Fields without AcroForm widgets can use coordinate stamps.";
    }
    void SaveJob()
    {
        if (Save("Portable PDF jobs|*.pdfjob", "pdfjob") is not { } file) return;
        BuildPackage(file); status.Text = "Saved job: " + file;
    }
    void CancelRun()
    {
        activeRun?.Cancel(); cancelButton.Enabled = false;
        status.Text = "Cancellation requested. Waiting for the current document to finish safely; the output will remain unchanged.";
    }
    async Task Fill()
    {
        if (activeRun is not null) return;
        if (Save("Filled PDF|*.pdf", "pdf") is not { } output) return;
        // Capture UI values before handing work to the background thread.
        var packageSource = package; var templatePath = template.Text;
        var requestText = request.Text; var assetsPath = assetRoot;
        using var cancellation = new CancellationTokenSource(); activeRun = cancellation;
        using var temp = new TemporaryDirectory();
        var job = Path.Combine(temp.Path, "job.pdfjob");
        foreach (var button in actionButtons) button.Enabled = false;
        request.ReadOnly = true; cancelButton.Enabled = true;
        status.Text = "Processing PDF locally. You can cancel; cancellation takes effect at a safe document boundary.";
        try
        {
            var warnings = new List<string>();
            await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                BuildPackage(job, packageSource, templatePath, requestText, assetsPath);
                cancellation.Token.ThrowIfCancellationRequested();
                JobService.FillJob(job, output, warnings, cancellation.Token);
            });
            status.Text = "Saved PDF: " + output + "\r\n" + string.Join("\r\n", warnings);
            if (!closeWhenIdle) Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
        }
        catch (OperationCanceledException) { status.Text = "Cancelled. The output was left unchanged."; }
        catch (Exception error)
        {
            status.Text = "Processing failed: " + error.Message;
            if (!closeWhenIdle) throw;
        }
        finally
        {
            activeRun = null; cancelButton.Enabled = false;
            foreach (var button in actionButtons) button.Enabled = true;
            request.ReadOnly = package is not null;
            if (closeWhenIdle) Close();
        }
    }
}

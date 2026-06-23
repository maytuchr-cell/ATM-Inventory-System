using System.Diagnostics;

// This exe is expected to live at the repo root, as a sibling of "Backend" and "Frontend".
string root = AppContext.BaseDirectory;
string repoRoot = FindRepoRoot(root) ?? Directory.GetCurrentDirectory();

string backendDir  = Path.Combine(repoRoot, "Backend", "Api");
string frontendDir = Path.Combine(repoRoot, "Frontend");

Console.WriteLine("=========================================");
Console.WriteLine("  ATM Inventory System — Launcher");
Console.WriteLine("=========================================");
Console.WriteLine($"Repo root : {repoRoot}");
Console.WriteLine();

if (!Directory.Exists(backendDir))
{
    Console.WriteLine($"ERROR: Backend folder not found at {backendDir}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}
if (!Directory.Exists(frontendDir))
{
    Console.WriteLine($"ERROR: Frontend folder not found at {frontendDir}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

Console.WriteLine("[1/3] Starting backend API (port 5128)...");
string dotnet = FindDotnet();
StartInNewWindow(dotnet, "run", backendDir, "ATM Inventory - Backend API");

Console.WriteLine("[2/3] Starting frontend server (port 3000)...");
string python = FindPython();
StartInNewWindow(python, "-m http.server 3000", frontendDir, "ATM Inventory - Frontend");

Console.WriteLine("[3/3] Waiting for servers to warm up...");
Thread.Sleep(5000);

Console.WriteLine("Opening browser...");
try
{
    Process.Start(new ProcessStartInfo
    {
        FileName = "http://localhost:3000/login.html",
        UseShellExecute = true
    });
}
catch (Exception ex)
{
    Console.WriteLine($"Could not auto-open browser: {ex.Message}");
    Console.WriteLine("Open this URL manually: http://localhost:3000/login.html");
}

Console.WriteLine();
Console.WriteLine("Done. Two new windows are running the backend and frontend servers.");
Console.WriteLine("Close those windows (or Ctrl+C inside them) to stop the system.");
Console.WriteLine();
Console.WriteLine("Press any key to close this launcher window...");
Console.ReadKey();

static void StartInNewWindow(string fileName, string arguments, string workingDirectory, string title)
{
    var psi = new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/k title {title} && cd /d \"{workingDirectory}\" && \"{fileName}\" {arguments}",
        WorkingDirectory = workingDirectory,
        UseShellExecute = true,
        CreateNoWindow = false,
        WindowStyle = ProcessWindowStyle.Normal
    };
    Process.Start(psi);
}

static string? FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Backend", "Api")) &&
            Directory.Exists(Path.Combine(dir.FullName, "Frontend")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return null;
}

static string FindDotnet()
{
    string[] candidates =
    {
        @"C:\Program Files\dotnet\dotnet.exe",
        @"C:\Program Files (x86)\dotnet\dotnet.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
    };
    foreach (var path in candidates)
        if (File.Exists(path)) return path;
    return "dotnet"; // fallback to PATH
}

static string FindPython()
{
    // The "python" on PATH is sometimes the Windows Store stub that does nothing.
    // Try the real install locations first, fall back to "python" on PATH.
    string[] candidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Python", "bin", "python.exe"),
        "python"
    };

    foreach (var path in candidates)
    {
        if (path == "python" || File.Exists(path))
            return path;
    }
    return "python";
}

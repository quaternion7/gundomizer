// Local, opt-in test host. Never included in the release package. No network listener.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

[BepInPlugin("quaternion.gundomizer.runtimeprobe", "Gundomizer Runtime Probe", "1.0.0")]
[BepInDependency("quaternion.gundomizer")]
public sealed class RuntimeProbe : BaseUnityPlugin
{
    private string directory;
    private float nextPoll;
    private bool busy;

    private void Awake()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-gundomizer-probe");
        if (index < 0 || index + 1 == args.Length) { enabled = false; return; }
        directory = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(directory);
        Log("READY " + Application.unityVersion + " args=" + string.Join(" ", args));
    }

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("o") + " " + message;
        File.AppendAllText(Path.Combine(directory, "probe.log"), line + Environment.NewLine);
        Logger.LogInfo("GUNDOMIZER_PROBE " + message);
    }

    private void Update()
    {
        if (busy || Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + 1f;
        string commandPath = Path.Combine(directory, "command.txt");
        if (!File.Exists(commandPath)) return;
        string command = File.ReadAllText(commandPath).Trim();
        File.Delete(commandPath);
        if (command == "quit") { Log("QUIT"); Application.Quit(); return; }
        if (command != "run") { Log("Unknown command: " + command); return; }
        try
        {
            // Load bytes to keep the file writable. The build script also gives each run a
            // unique assembly identity to avoid this Unity Mono version's assembly cache.
            var assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(directory, "suite.dll")));
            var method = assembly.GetType("RuntimeSuite", true).GetMethod("Run");
            var routine = (IEnumerator)method.Invoke(null, new object[] { directory, new Action<string>(Log) });
            busy = true;
            StartCoroutine(Guard(routine));
        }
        catch (Exception ex) { busy = false; Log("FAIL " + ex); }
    }

    private IEnumerator Guard(IEnumerator routine)
    {
        try
        {
            while (true)
            {
                object current;
                try { if (!routine.MoveNext()) break; current = routine.Current; }
                catch (Exception ex) { Log("FAIL " + ex); break; }
                yield return current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); busy = false; Log("RUN COMPLETE"); }
    }
}

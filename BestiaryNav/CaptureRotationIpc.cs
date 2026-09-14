using System;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace BestiaryNav;

// Mutations use Rotation Solver Reborn's published IPC exclusively. It has no
// getters for the specific operating mode/loaded rotation; managed, read-only
// reflection fills that gap and fails closed if expected properties disappear.
// https://github.com/FFXIV-CombatReborn/RotationSolverReborn/blob/main/RotationSolver/IPC/IPCProvider.cs
internal sealed class CaptureRotationIpc : IRotationSolverBackend, IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICallGateSubscriber<bool> active;
    private readonly ICallGateSubscriber<byte, object> changeMode;
    private readonly RotationSolverControl control;
    private SolverStateReader? reader;

    public CaptureRotationIpc(IDalamudPluginInterface pi)
    {
        this.pi = pi;
        active = pi.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive");
        changeMode = pi.GetIpcSubscriber<byte, object>("RotationSolverReborn.ChangeOperatingMode");
        control = new(this);
    }

    public bool Available => active.HasFunction && changeMode.HasAction &&
        pi.InstalledPlugins.Any(p => p.InternalName == "RotationSolver" && p.IsLoaded);

    public void Acquire(uint job)
    {
        if (job != 43) throw new InvalidOperationException("Capture runs require BST.");
        if (!Available) throw new InvalidOperationException("Enable Rotation Solver Reborn with BST support (7.5.6.8 or later).");
        reader = new(() => pi.InstalledPlugins.SingleOrDefault(p => p.InternalName == "RotationSolver" && p.IsLoaded)?.Version);
        control.Acquire();
    }

    public void Verify(bool requireRotation = true) => control.Verify(requireRotation);
    public void SetRunning(bool value) => control.SetRunning(value);
    public void Restart(Action opener) => control.Restart(opener);
    public void Release() { try { control.Release(); } finally { reader = null; } }
    public void Dispose() => Release();

    RotationSolverState IRotationSolverBackend.Read()
    {
        // Read mode even during logout: AutorotationActive() returns false when
        // the player disappears, although a controlled mode may still need Off.
        return (reader ?? throw new InvalidOperationException("Rotation Solver status is unavailable.")).Read();
    }

    void IRotationSolverBackend.ChangeMode(RotationSolverMode mode) => changeMode.InvokeAction((byte)mode);

    private sealed class SolverStateReader
    {
        private readonly Assembly assembly;
        private readonly Func<Version?> installedVersion;
        private readonly Func<bool> state, manual, henched, targetOnly, autoDuty, pvp;
        private readonly PropertyInfo rotation, config, teaching, autoOn;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Assembly CurrentAssembly()
        {
            var version = installedVersion() ?? throw new InvalidOperationException("Rotation Solver was unloaded.");
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "RotationSolver.Basic" &&
                a.GetName().Version == version).ToArray();
            if (assemblies.Length != 1)
                throw new InvalidOperationException("Rotation Solver was reloaded or its status is ambiguous. Restart the game before a capture run.");
            return assemblies[0];
        }

        public SolverStateReader(Func<Version?> installedVersion)
        {
            this.installedVersion = installedVersion;
            assembly = CurrentAssembly();
            var center = assembly.GetType("RotationSolver.Basic.DataCenter", true)!;
            state = Getter(center, "State"); manual = Getter(center, "IsManual"); henched = Getter(center, "IsHenched");
            targetOnly = Getter(center, "IsTargetOnly"); autoDuty = Getter(center, "IsAutoDuty"); pvp = Getter(center, "IsPvPStateEnabled");
            rotation = Property(center, "CurrentRotation", Static);
            config = Property(assembly.GetType("RotationSolver.Basic.Service", true)!, "Config", Static);
            teaching = Property(config.PropertyType, "TeachingMode", BindingFlags.Public | BindingFlags.Instance);
            autoOn = Property(config.PropertyType, "AutoOnYes", BindingFlags.Public | BindingFlags.Instance);
            var commands = assembly.GetType("RotationSolver.Basic.Data.StateCommandType", true)!;
            if (Convert.ToByte(Enum.Parse(commands, "Off")) != (byte)RotationSolverMode.Off ||
                Convert.ToByte(Enum.Parse(commands, "Henched")) != (byte)RotationSolverMode.Capture)
                throw new InvalidOperationException("Rotation Solver's control API changed. Update Bestiary Nav.");
        }

        public RotationSolverState Read()
        {
            if (CurrentAssembly() != assembly) throw new InvalidOperationException("Rotation Solver was reloaded. Capture run stopped.");
            var isBst = false;
            for (var type = rotation.GetValue(null)?.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "RotationSolver.Basic.Rotations.Basic.BeastmasterRotation") isBst = true;
            var settings = config.GetValue(null) ?? throw new InvalidOperationException("Rotation Solver settings are not loaded.");
            return new(state(), manual(), henched(), targetOnly(), autoDuty(), pvp(), isBst,
                Boolean(teaching.GetValue(settings)), Boolean(autoOn.GetValue(settings)));
        }

        private static PropertyInfo Property(Type type, string name, BindingFlags flags) => type.GetProperty(name, flags) ??
            throw new InvalidOperationException($"Rotation Solver status '{name}' changed. Update Bestiary Nav.");
        private static Func<bool> Getter(Type type, string name) =>
            Property(type, name, Static).GetGetMethod(true)!.CreateDelegate<Func<bool>>();
        private static bool Boolean(object? value)
        {
            if (value is bool boolean) return boolean;
            if (value != null)
            {
                var conversion = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .SingleOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(bool) &&
                        m.GetParameters() is { Length: 1 } p && p[0].ParameterType == value.GetType());
                if (conversion?.Invoke(null, [value]) is bool result) return result;
            }
            throw new InvalidOperationException("Rotation Solver's settings format changed. Update Bestiary Nav.");
        }
    }
}

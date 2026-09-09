using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace BestiaryNav;

// Read the UI-filtered input before movement automation changes the movement vector.
// Never infer manual input from player position: vnavmesh changes that too.
internal static class ManualMovementInput
{
    private static readonly InputId[] MovementBindings =
    [
        InputId.MOVE_FORE, InputId.MOVE_BACK, InputId.MOVE_LEFT, InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L, InputId.MOVE_STRIFE_R, InputId.MOVE_AND_STEER,
    ];

    public static unsafe bool Read()
    {
        var framework = GameFramework.Instance();
        if (framework == null || framework->WindowInactive) return false;
        var input = UIInputData.Instance();
        if (input == null) return false;
        var io = ImGui.GetIO();
        // Native chat filters KeyboardInputs; plugin text fields need this extra guard.
        if (!io.WantTextInput)
            foreach (var binding in MovementBindings)
                if (input->IsInputIdDown(binding)) return true;

        // Values range from -99 to 99. Ignore small amounts of stick drift.
        var stick = input->GamepadInputs;
        if (stick.LeftStickX is < -20 or > 20 || stick.LeftStickY is < -20 or > 20) return true;

        const MouseButtonFlags both = MouseButtonFlags.LBUTTON | MouseButtonFlags.RBUTTON;
        return !io.WantCaptureMouse && (input->UIFilteredCursorInputs.MouseButtonHeldFlags & both) == both;
    }
}

using System.Runtime.InteropServices;
using System.Numerics;
using BestiaryNav;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;

// This isolated process exercises the actual guarded reader on allocated native
// fixtures. It never injects into, writes to, or calls functions in the game.
unsafe
{
    var allocations = new List<nint>();
    int checks = 0;
    T* Allocate<T>(int count = 1) where T : unmanaged
    {
        var pointer = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
        allocations.Add((nint)pointer);
        return pointer;
    }
    void Check(bool result, string name)
    {
        if (!result) throw new Exception(name);
        checks++;
    }
    try
    {
        var finder = Allocate<AgentContentsFinder>();
        var entries = Allocate<Pointer<Contents>>(3);
        finder->ContentList.First = entries;
        finder->ContentList.Last = finder->ContentList.End = entries + 3;
        for (var i = 0; i < 3; i++)
        {
            var entry = Allocate<Contents>();
            entries[i] = entry;
            entry->Id = new() { ContentType = ContentsType.Regular, Id = (uint)(i + 3) };
        }
        entries[0].Value->Id.ContentType = ContentsType.Roulette;
        entries[2].Value->Id.Id = 3;
        Check(DutySelectionReader.TryFindEntry(finder, 3, out var dutyIndex) && dutyIndex == 3, "duty callback index is one-based and excludes same-ID roulette");
        Check(!DutySelectionReader.TryFindEntry(finder, 0, out _) && !DutySelectionReader.TryFindEntry(finder, 999, out _), "invalid or absent duty cannot be selected");
        entries[1].Value->Id.Id = 3;
        Check(!DutySelectionReader.TryFindEntry(finder, 3, out _), "duplicate matching duties rejected");
        entries[1].Value->Id.Id = 4;
        var selected = Allocate<ContentsId>(6);
        finder->SelectedContent.First = selected;
        finder->SelectedContent.Last = selected;
        finder->SelectedContent.End = selected + 6;
        Check(DutySelectionReader.TryReadSelection(finder, 3, out var selectedCount, out var onlyTarget) && selectedCount == 0 && !onlyTarget, "cleared selection verified");
        selected[0] = new() { ContentType = ContentsType.Regular, Id = 3 };
        finder->SelectedContent.Last = selected + 1;
        Check(DutySelectionReader.TryReadSelection(finder, 3, out selectedCount, out onlyTarget) && selectedCount == 1 && onlyTarget, "target-only selection verified");
        selected[0].ContentType = ContentsType.Roulette;
        Check(DutySelectionReader.TryReadSelection(finder, 3, out _, out onlyTarget) && !onlyTarget, "roulette with same ID is not target success");
        selected[0].ContentType = ContentsType.Regular;
        finder->SelectedContent.Last = selected + 2;
        Check(DutySelectionReader.TryReadSelection(finder, 3, out selectedCount, out onlyTarget) && selectedCount == 2 && !onlyTarget, "extra checked duties prevent success");
        finder->SelectedContent.Last = selected + 6;
        Check(!DutySelectionReader.TryReadSelection(finder, 3, out _, out _), "invalid selection size rejected");
        finder->ContentList.Last = entries + 2049;
        Check(!DutySelectionReader.TryFindEntry(finder, 3, out _), "oversized content list rejected before traversal");
        finder->ContentList.Last = entries + 3;
        entries[1] = (Contents*)0x12345678;
        Check(!DutySelectionReader.TryFindEntry(finder, 3, out _), "unreadable content pointer rejected safely");
        Check(!DutySelectionReader.TryFindEntry(null, 3, out _) && !DutySelectionReader.TryReadSelection(null, 3, out _, out _), "missing agent safely rejected");
        Check(!NativeSnapshot.TryRead<long>(0, out _), "null pointer rejected");
        Check(!NativeSnapshot.TryRead<long>(0x12345678, out _), "unmapped memory returns false");
        var addon = Allocate<AtkUnitBase>();
        var list = Allocate<nint>(8);
        addon->UldManager.NodeList = (AtkResNode**)list;
        addon->UldManager.NodeListCount = 8;
        var rows = new nint[8];
        var events = new nint[8];
        for (var i = 0; i < 8; i++)
        {
            var row = Allocate<AtkComponentNode>();
            var component = Allocate<AtkComponentBase>();
            var collision = Allocate<AtkResNode>();
            var registration = Allocate<AtkEvent>();
            var children = Allocate<nint>(2);
            var name = Allocate<AtkResNode>();
            // Reverse traversal order, as observed in the real UldManager list.
            list[7 - i] = rows[i] = (nint)row;
            row->NodeId = i == 0 ? 2u : 20000u + (uint)i;
            row->Type = (NodeType)1001;
            row->NodeFlags = NodeFlags.Visible;
            row->ScreenX = 500;
            row->ScreenY = 100 + 32 * i;
            row->Width = 224;
            row->Transform.M11 = 1;
            row->Component = component;
            component->OwnerNode = row;
            component->UldManager.NodeList = (AtkResNode**)children;
            component->UldManager.NodeListCount = 2;
            *children = (nint)collision;
            children[1] = (nint)name;
            name->NodeId = 6;
            name->Type = NodeType.Text;
            name->Height = 20;
            name->Transform.M22 = 1;
            name->ScreenY = row->ScreenY + 2;
            collision->NodeId = 19;
            collision->Type = NodeType.Collision;
            collision->AtkEventManager.Event = registration;
            events[i] = (nint)registration;
            registration->State.EventType = AtkEventType.MouseDown;
            registration->Param = (uint)i;
            registration->Listener = (AtkEventListener*)addon;
        }
        for (var i = 0; i < 8; i++)
            Check(EnemyListRows.TryGetBounds((nint)addon, i, out var anchor) && anchor == new EnemyRowBounds(500, 724, 112 + 32 * i), $"correct row {i}");
        Check(!EnemyListRows.TryGetBounds((nint)addon, -1, out _) && !EnemyListRows.TryGetBounds((nint)addon, 8, out _), "row bounds");
        var first = (AtkComponentNode*)rows[0];
        first->Transform.M11 = 1.5f;
        Check(EnemyListRows.TryGetBounds((nint)addon, 0, out var scaled) && scaled.Right == 836, "scaled row width");
        first->Transform.M11 = 1;
        var textSize = new Vector2(80, 16);
        var viewportSize = new Vector2(1920, 1080);
        Check(EnemyLabelLayout.TryPlace(new(67, 291, 727), textSize, Vector2.Zero, viewportSize, out var label) && label == new Vector2(304, 719), "left-edge HUD places label to right on name baseline");
        Check(EnemyLabelLayout.TryPlace(new(1670, 1894, 727), textSize, Vector2.Zero, viewportSize, out label) && label == new Vector2(1577, 719), "right-edge HUD falls back to left");
        Check(EnemyLabelLayout.TryPlace(new(67, 291, 727), textSize, new(100, 50), viewportSize, out label) && label == new Vector2(404, 769), "window origin added once");
        Check(EnemyLabelLayout.TryPlace(new(-100, 5, -10), textSize, Vector2.Zero, viewportSize, out label) && label.Y == 7, "top border includes background padding");
        Check(EnemyLabelLayout.TryPlace(new(1900, 2200, 1100), textSize, Vector2.Zero, viewportSize, out label) && label.Y == 1057, "bottom border includes background padding");
        Check(!EnemyLabelLayout.TryPlace(new(0, 224, 10), textSize, Vector2.Zero, new(60, 20), out _), "tiny viewport skipped");
        Check(!EnemyLabelLayout.TryPlace(new(float.NaN, 224, 10), textSize, Vector2.Zero, viewportSize, out _), "invalid geometry skipped");
        var second = (AtkComponentNode*)rows[1];
        second->NodeFlags = 0;
        Check(!EnemyListRows.TryGetBounds((nint)addon, 1, out _), "hidden row");
        second->NodeFlags = NodeFlags.Visible;
        var saved = second->Component;
        second->Component = (AtkComponentBase*)0x12345678;
        Check(!EnemyListRows.TryGetBounds((nint)addon, 1, out _), "invalid component safely skipped");
        second->Component = saved;
        saved->OwnerNode = (AtkComponentNode*)rows[0];
        Check(!EnemyListRows.TryGetBounds((nint)addon, 1, out _), "wrong owner rejected");
        saved->OwnerNode = second;
        ((AtkEvent*)events[1])->Param = 0;
        Check(!EnemyListRows.TryGetBounds((nint)addon, 1, out _), "wrong event index rejected");
        ((AtkEvent*)events[1])->NextEvent = (AtkEvent*)events[1];
        Check(!EnemyListRows.TryGetBounds((nint)addon, 1, out _), "cyclic event list bounded");
        addon->UldManager.NodeListCount = 129;
        Check(!EnemyListRows.TryGetBounds((nint)addon, 0, out _), "excessive node count rejected");
        Console.WriteLine($"Passed {checks} native row regression checks; live rendering still requires in-game verification.");
    }
    finally { foreach (var pointer in allocations) NativeMemory.Free((void*)pointer); }
}

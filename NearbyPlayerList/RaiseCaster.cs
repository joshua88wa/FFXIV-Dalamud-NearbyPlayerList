using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace NearbyPlayerList;

// Raising from the list. A click is one user action, but Swiftcast and the raise are
// two separate casts, so this holds a small pending state and fires the raise on a
// later frame once the instant-cast buff has actually landed.
public static unsafe class RaiseCaster
{
    private const uint SwiftcastAction = 7561;
    private const uint SwiftcastStatus = 167;
    private const uint DualcastStatus = 1249;

    // Nothing sensible can take this long. Without a deadline a failed Swiftcast would
    // leave a raise queued to fire at whoever is targeted much later.
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(3);

    private static uint pendingAction;
    private static ulong pendingTarget;
    private static DateTime pendingExpires;

    public static bool HasPending => pendingAction != 0;

    // The raise this job would actually use. Several may report usable at once, for
    // example a phantom job raise alongside a job one, so a match on the current job
    // wins and anything else is a fallback.
    public static uint? PickRaise(IReadOnlyCollection<uint> raiseActions)
    {
        var local = Service.Objects.LocalPlayer;
        if (local == null)
            return null;

        var manager = ActionManager.Instance();
        if (manager == null)
            return null;

        var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var job = local.ClassJob.RowId;

        uint? fallback = null;

        foreach (var id in raiseActions)
        {
            if (manager->GetActionStatus(ActionType.Action, id) != 0)
                continue;

            var row = sheet?.GetRowOrDefault(id);
            if (row != null && row.Value.ClassJob.RowId == job)
                return id;

            fallback ??= id;
        }

        return fallback;
    }

    // True when the raise would go off without a cast bar: the action is already
    // instant, or an instant-cast buff is up. Red Mage's Dualcast counts, so a Red Mage
    // mid-Dualcast is not told to burn Swiftcast.
    public static bool InstantReady(IReadOnlyCollection<uint> raiseActions, Configuration config)
    {
        var action = PickRaise(raiseActions);
        if (action == null)
            return false;

        if (CastTimeOf(action.Value) == 0)
            return true;

        if (HasInstantBuff())
            return true;

        return config.UseSwiftcast && SwiftcastReady();
    }

    public static bool SwiftcastReady()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->GetActionStatus(ActionType.Action, SwiftcastAction) == 0;
    }

    private static bool HasInstantBuff()
    {
        if (Service.Objects.LocalPlayer is not IBattleChara self)
            return false;

        foreach (var status in self.StatusList)
        {
            if (status.StatusId is SwiftcastStatus or DualcastStatus)
                return true;
        }

        return false;
    }

    private static ushort CastTimeOf(uint actionId)
    {
        var row = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(actionId);
        return row?.Cast100ms ?? 0;
    }

    public static void Begin(PlayerEntry entry, Configuration config, IReadOnlyCollection<uint> raiseActions)
    {
        var action = PickRaise(raiseActions);
        if (action == null)
        {
            Service.Chat.Print("[NPL] No raise available right now.");
            return;
        }

        var manager = ActionManager.Instance();
        if (manager == null)
            return;

        Service.Targets.Target = entry.GameObject;

        var targetId = entry.GameObject.GameObjectId;
        var needsSwiftcast = CastTimeOf(action.Value) > 0 && !HasInstantBuff();

        if (needsSwiftcast && config.UseSwiftcast && SwiftcastReady())
        {
            var self = Service.Objects.LocalPlayer;
            manager->UseAction(ActionType.Action, SwiftcastAction, self?.GameObjectId ?? 0xE000_0000);

            // The buff is not up in the same frame the button is pressed, so the raise
            // waits for Tick to see it land.
            pendingAction = action.Value;
            pendingTarget = targetId;
            pendingExpires = DateTime.UtcNow + PendingTimeout;
            return;
        }

        manager->UseAction(ActionType.Action, action.Value, targetId);
    }

    public static void Tick()
    {
        if (pendingAction == 0)
            return;

        if (DateTime.UtcNow > pendingExpires)
        {
            Clear();
            return;
        }

        if (!HasInstantBuff())
            return;

        var manager = ActionManager.Instance();
        if (manager != null)
            manager->UseAction(ActionType.Action, pendingAction, pendingTarget);

        Clear();
    }

    public static void Clear()
    {
        pendingAction = 0;
        pendingTarget = 0;
    }
}

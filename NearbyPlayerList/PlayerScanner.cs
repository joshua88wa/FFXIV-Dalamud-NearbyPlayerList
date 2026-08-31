using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace NearbyPlayerList;

public sealed class PlayerEntry
{
    public uint EntityId;
    public string Name = string.Empty;
    public uint JobId;
    public byte Role;              // 0 other, 1 tank, 2 melee dps, 3 ranged dps, 4 healer
    public uint CurrentHp;
    public uint MaxHp;
    public bool IsDead;
    public bool IsSelf;
    public bool IsPartyMember;
    public bool IsTarget;
    public bool IsSoftTarget;
    public bool IsFocusTarget;
    public bool AlreadyRaised;
    public string? RaisedBy;       // non-null when someone is casting a raise on this player
    public float Distance;
    public IGameObject GameObject = null!;

    public float HpFraction => this.MaxHp == 0 ? 0f : (float)this.CurrentHp / this.MaxHp;
    public uint MissingHp => this.MaxHp > this.CurrentHp ? this.MaxHp - this.CurrentHp : 0;
    public bool RaiseHandled => this.AlreadyRaised || this.RaisedBy != null;
}

public sealed class PlayerScanner
{
    // Status IDs that mean "a raise is already pending on this corpse".
    // Concept and status IDs cross-checked against RezPls by Ottermandias
    // (AGPL-3.0) - https://github.com/Ottermandias/RezPls
    // No code from that project is used here; this is an independent implementation.
    private static readonly uint[] RaiseStatusIds = { 148, 1140 };

    // Known player raise actions. The set is also extended at load time with every
    // player action the game marks as able to target the dead, so job actions added
    // in later patches (including Field Operation / Phantom Job raises) are picked up
    // without a code change.
    private static readonly uint[] KnownRaiseActions =
    {
        125,    // Raise (WHM/CNJ)
        173,    // Resurrection (SCH/SMN/ACN)
        3603,   // Ascend (AST)
        7523,   // Verraise (RDM)
        18317,  // Angel Whisper (BLU)
        24287,  // Egeiro (SGE)
    };

    private readonly HashSet<uint> raiseActions = new(KnownRaiseActions);
    private readonly Configuration config;

    public PlayerScanner(Configuration config)
    {
        this.config = config;
        this.TryExtendRaiseActions();
    }

    private void TryExtendRaiseActions()
    {
        try
        {
            var sheet = Service.Data.GetExcelSheet<LuminaAction>();
            if (sheet == null)
                return;

            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                    continue;
                // DeadTargetBehaviour == 1 marks actions that can be used on a corpse.
                // Verified against the 7.5 Action sheet: it selects the six job raises
                // plus their duty/Field Operation variants, including Occult Raise,
                // Phoenix Down and Variant Raise, and nothing else of consequence.
                // Note that those variants are NOT flagged IsPlayerAction, so filtering
                // on that would miss the Occult Crescent case entirely.
                if (row.DeadTargetBehaviour == 1)
                    this.raiseActions.Add(row.RowId);
            }
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not extend the raise action list from the Action sheet; using the built-in list only.");
        }
    }

    public List<PlayerEntry> Scan()
    {
        var result = new List<PlayerEntry>();

        var local = Service.Objects.LocalPlayer;
        if (local == null)
            return result;

        var casters = this.config.ShowRaiseInProgress ? this.CollectRaiseCasts() : null;
        var partyIds = this.CollectPartyIds();
        var targetId = Service.Targets.Target?.EntityId ?? 0;
        var softTargetId = Service.Targets.SoftTarget?.EntityId ?? 0;
        var focusTargetId = Service.Targets.FocusTarget?.EntityId ?? 0;

        foreach (var obj in Service.Objects)
        {
            if (obj is not IPlayerCharacter pc)
                continue;
            if (!pc.IsTargetable)
                continue;

            var isSelf = pc.EntityId == local.EntityId;
            if (isSelf && this.config.HideSelf)
                continue;

            var isParty = partyIds.Contains(pc.EntityId);
            if (isParty && this.config.HideParty && !isSelf)
                continue;

            var distance = Vector3.Distance(local.Position, pc.Position);
            if (this.config.LimitDistance && distance > this.config.MaxDistance)
                continue;

            var entry = new PlayerEntry
            {
                EntityId = pc.EntityId,
                Name = pc.Name.TextValue,
                JobId = pc.ClassJob.RowId,
                Role = SafeRole(pc),
                CurrentHp = pc.CurrentHp,
                MaxHp = pc.MaxHp,
                IsDead = pc.CurrentHp == 0,
                IsSelf = isSelf,
                IsPartyMember = isParty,
                Distance = distance,
                GameObject = pc,
                IsTarget = targetId != 0 && pc.EntityId == targetId,
                IsSoftTarget = softTargetId != 0 && pc.EntityId == softTargetId,
                IsFocusTarget = focusTargetId != 0 && pc.EntityId == focusTargetId,
            };

            if (entry.IsDead)
            {
                entry.AlreadyRaised = HasRaiseStatus(pc);
                if (casters != null && casters.TryGetValue(pc.EntityId, out var caster))
                    entry.RaisedBy = caster;
            }

            if (!this.PassesFilter(entry))
                continue;

            result.Add(entry);
        }

        this.Sort(result);

        if (this.config.MaxPlayers > 0 && result.Count > this.config.MaxPlayers)
            result.RemoveRange(this.config.MaxPlayers, result.Count - this.config.MaxPlayers);

        return result;
    }

    private static byte SafeRole(IPlayerCharacter pc)
    {
        try
        {
            return pc.ClassJob.ValueNullable?.Role ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool HasRaiseStatus(IBattleChara chara)
    {
        foreach (var status in chara.StatusList)
        {
            foreach (var id in RaiseStatusIds)
            {
                if (status.StatusId == id)
                    return true;
            }
        }

        return false;
    }

    private Dictionary<uint, string> CollectRaiseCasts()
    {
        var map = new Dictionary<uint, string>();

        foreach (var obj in Service.Objects)
        {
            if (obj is not IBattleChara chara)
                continue;
            if (!chara.IsCasting)
                continue;
            if (!this.raiseActions.Contains(chara.CastActionId))
                continue;

            var targetId = (uint)chara.CastTargetObjectId;
            if (targetId == 0 || targetId == 0xE0000000)
                continue;

            map[targetId] = chara.Name.TextValue;
        }

        return map;
    }

    private HashSet<uint> CollectPartyIds()
    {
        var ids = new HashSet<uint>();

        foreach (var member in Service.Party)
        {
            var go = member.GameObject;
            if (go != null)
                ids.Add(go.EntityId);
        }

        return ids;
    }

    private bool PassesFilter(PlayerEntry entry)
    {
        switch (this.config.Filter)
        {
            case FilterMode.BelowHealthThreshold:
                if (entry.MaxHp == 0)
                    return false;
                if (entry.HpFraction > this.config.HealthThreshold)
                    return false;
                if (entry.IsDead && this.config.FilterIgnoreAlreadyRaised && entry.RaiseHandled)
                    return false;
                return true;

            case FilterMode.DeadOnly:
                if (!entry.IsDead)
                    return false;
                if (this.config.FilterIgnoreAlreadyRaised && entry.RaiseHandled)
                    return false;
                return true;

            case FilterMode.All:
            default:
                return true;
        }
    }

    private void Sort(List<PlayerEntry> list)
    {
        Comparison<PlayerEntry> baseSort = this.config.Sort switch
        {
            SortMode.Alphabetical => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            // Missing HP is compared as a fraction of max HP rather than as a raw
            // number, so a tank sitting on a large pool does not outrank a squishier
            // player who is proportionally closer to dying.
            SortMode.MissingHp => (a, b) => a.HpFraction.CompareTo(b.HpFraction),
            _ => (a, b) =>
            {
                var ra = RoleOrder(a.Role);
                var rb = RoleOrder(b.Role);
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
        };

        list.Sort((a, b) =>
        {
            if (this.config.DeadAtTop)
            {
                var da = a.IsDead && !(this.config.SortIgnoreAlreadyRaised && a.RaiseHandled);
                var db = b.IsDead && !(this.config.SortIgnoreAlreadyRaised && b.RaiseHandled);
                if (da != db)
                    return da ? -1 : 1;
            }

            if (this.config.PartyAtTop && !this.config.HideParty)
            {
                if (this.config.SelfAboveParty && a.IsSelf != b.IsSelf)
                    return a.IsSelf ? -1 : 1;

                // You count as a party member for this rule even when solo. The party
                // list is empty outside a party, so keying purely off it would leave
                // you sorted in with everyone else exactly when you are easiest to
                // lose track of.
                var partyA = a.IsPartyMember || a.IsSelf;
                var partyB = b.IsPartyMember || b.IsSelf;
                if (partyA != partyB)
                    return partyA ? -1 : 1;
            }

            return baseSort(a, b);
        });
    }

    // Tank, healer, then dps, matching the party list convention.
    private static int RoleOrder(byte role) => role switch
    {
        1 => 0,   // tank
        4 => 1,   // healer
        2 => 2,   // melee dps
        3 => 3,   // ranged dps
        _ => 4,
    };
}







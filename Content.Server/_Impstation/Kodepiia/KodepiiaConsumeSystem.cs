using Content.Server.Actions;
using Content.Server.Atmos.Rotting;
using Content.Server.Body.Systems;
using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Shared.Body.Components;
using Content.Shared.Changeling;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Impstation.Kodepiia;

public sealed partial class KodepiiaConsumeSystem : Shared._Impstation.Kodepiia.SharedKodepiiaConsumeSystem
{
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IRobustRandom _rand = default!;
    [Dependency] private readonly RottingSystem _rotting = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent, KodepiiaConsumeEvent>(Consume);
        SubscribeLocalEvent<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent, KodepiiaConsumeDoAfterEvent>(ConsumeDoafter);
    }

    public void Consume(Entity<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent> ent, ref KodepiiaConsumeEvent args)
    {
        if (!HasComp<AbsorbableComponent>(args.Target) || _rotting.IsRotten(args.Target))
        {
            SetActionCooldown(ent,5);
            _popup.PopupEntity(Loc.GetString("kodepiia-consume-fail-inedible", ("target", Identity.Entity(args.Target, EntityManager))), ent, ent);
            return;
        }

        if (!_mobState.IsIncapacitated(args.Target))
        {
            SetActionCooldown(ent,5);
            _popup.PopupEntity(Loc.GetString("kodepiia-consume-fail-incapacitated", ("target", Identity.Entity(args.Target, EntityManager))), ent, ent);
            return;
        }

        PlayMeatySound(ent);

        if (!TryComp<PhysicsComponent>(args.Target, out var targetPhysics))
            return;

        var doargs = new DoAfterArgs(EntityManager, ent, targetPhysics.Mass/5, new KodepiiaConsumeDoAfterEvent(), ent, args.Target)
        {
            DistanceThreshold = 1.5f,
            BreakOnDamage = true,
            BreakOnHandChange = false,
            BreakOnMove = true,
            BreakOnWeightlessMove = true,
            AttemptFrequency = AttemptFrequency.StartAndEnd
        };

        var popupSelf = Loc.GetString("kodepiia-consume-start-self", ("user", Identity.Entity(ent, EntityManager)), ("target", Identity.Entity(args.Target, EntityManager)));
        var popupOthers = Loc.GetString("kodepiia-consume-start-others", ("user", Identity.Entity(ent, EntityManager)), ("target", Identity.Entity(args.Target, EntityManager)));

        _popup.PopupEntity(popupSelf, ent, ent);
        _popup.PopupEntity(popupOthers, ent, Filter.Pvs(ent).RemovePlayersByAttachedEntity(ent), true, PopupType.MediumCaution);

        _doAfter.TryStartDoAfter(doargs);
        args.Handled = true;
    }

    public void ConsumeDoafter(Entity<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent> ent, ref KodepiiaConsumeDoAfterEvent args)
    {
        if (args.Target == null)
        {
            SetActionCooldown(ent, 5);
            return;
        }

        if (!TryComp<PhysicsComponent>(args.Target, out var targetPhysics))
            return;

        if (args.Cancelled)
            return;

        // Drink Bloodstream
        if (_solutionContainer.TryGetSolution(args.User, "chemicals", out var userSolutionComp, out var userSolution)
            && _solutionContainer.TryGetSolution(args.Target.Value, "bloodstream", out var targetSolutionComp, out var targetSolution))
        {
            if (userSolution.Volume < userSolution.MaxVolume)
            {
                var transferAmount = targetSolution.MaxVolume / 10;
                var realTransferAmount =
                    FixedPoint2.Min(transferAmount, userSolution.AvailableVolume);
                // how much protein to add to the mix
                var protein = targetPhysics.Mass / 2;
                if (realTransferAmount.Value > 0)
                {
                    var removedSolution =
                        _solutionContainer.SplitSolution(targetSolutionComp.Value, realTransferAmount);
                    removedSolution.AddReagent("UncookedAnimalProteins",protein);
                    _solutionContainer.TryAddSolution(userSolutionComp.Value, removedSolution);
                }
            }
        }
        // Deal Damage
        _damage.TryChangeDamage(args.Target, ent.Comp.Damage, true, false);

        // Play Sound
        PlayMeatySound(ent);

        var popupSelf = Loc.GetString("kodepiia-consume-end-self", ("user", Identity.Entity(ent, EntityManager)), ("target", Identity.Entity(args.Target.Value, EntityManager)));
        var popupOthers = Loc.GetString("kodepiia-consume-end-others", ("user", Identity.Entity(ent, EntityManager)), ("target", Identity.Entity(args.Target.Value, EntityManager)));
        _popup.PopupEntity(popupSelf, ent, ent);
        _popup.PopupEntity(popupOthers, ent, Filter.Pvs(ent).RemovePlayersByAttachedEntity(ent), true, PopupType.LargeCaution);

        //Consumed Componentry Stuff lol
        EnsureComp<Shared._Impstation.Kodepiia.Components.KodepiiaConsumedComponent>(args.Target.Value, out var consumed);
        consumed.TimesConsumed += 1;
        if (consumed.TimesConsumed >= 12 && TryComp<BodyComponent>(args.Target.Value, out var body) && ent.Comp.Gib)
        {
            _body.GibBody(args.Target.Value,true,body);
        }
    }
    public void SetActionCooldown(Entity<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent> ent, int cooldown)
    {
        _actionsSystem.SetCooldown(ent.Comp.ConsumeAction, TimeSpan.FromSeconds(cooldown));
    }

    public void PlayMeatySound(Entity<Shared._Impstation.Kodepiia.Components.KodepiiaConsumeActionComponent> ent)
    {
        var rand = _rand.Next(0, ent.Comp.SoundPool.Count - 1);
        var sound = ent.Comp.SoundPool.ToArray()[rand];
        _audio.PlayPvs(sound, ent, AudioParams.Default.WithVolume(-3f));
    }
}

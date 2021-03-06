﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Channel.Network.Sending;
using Aura.Channel.Skills.Base;
using Aura.Channel.Skills.Magic;
using Aura.Channel.World;
using Aura.Channel.World.Entities;
using Aura.Data.Database;
using Aura.Mabi.Const;
using Aura.Mabi.Network;
using Aura.Shared.Network;
using Aura.Shared.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aura.Channel.Skills.Combat
{
	/// <summary>
	/// Smash skill handler.
	/// </summary>
	/// <remarks>
	/// Var1: Damage
	/// Var2: ? (0, not used?)
	/// Var3: Critical Success (0, not used?)
	/// Var4: Splash Damage
	/// </remarks>
	[Skill(SkillId.Smash)]
	public class Smash : CombatSkillHandler, IInitiableSkillHandler
	{
		/// <summary>
		/// Stuntime in ms for attacker and target.
		/// </summary>
		private const int StunTime = 3000;

		/// <summary>
		/// Stuntime in ms after usage...?
		/// (Really? Then what's that ^?)
		/// </summary>
		private const int AfterUseStun = 600;

		/// <summary>
		/// Units the enemy is knocked back.
		/// </summary>
		private const int KnockbackDistance = 450;

		/// <summary>
		/// Subscribes handlers to events required for training.
		/// </summary>
		public void Init()
		{
			ChannelServer.Instance.Events.CreatureAttackedByPlayer += this.OnCreatureAttackedByPlayer;
		}

		/// <summary>
		/// Prepares skill, called to start casting it.
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="skill"></param>
		/// <param name="packet"></param>
		public override bool Prepare(Creature creature, Skill skill, Packet packet)
		{
			Send.SkillFlashEffect(creature);
			Send.SkillPrepare(creature, skill.Info.Id, skill.GetCastTime());

			return true;
		}

		/// <summary>
		/// Readies skill, called when casting is done.
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="skill"></param>
		/// <param name="packet"></param>
		public override bool Ready(Creature creature, Skill skill, Packet packet)
		{
			Send.SkillReady(creature, skill.Info.Id);

			return true;
		}

		/// <summary>
		/// Completes skill usage, called after it was used successfully.
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="skill"></param>
		/// <param name="packet"></param>
		public override void Complete(Creature creature, Skill skill, Packet packet)
		{
			Send.SkillComplete(creature, skill.Info.Id);
		}

		/// <summary>
		/// Handles skill usage.
		/// </summary>
		/// <param name="attacker"></param>
		/// <param name="skill"></param>
		/// <param name="targetEntityId"></param>
		/// <returns></returns>
		public override CombatSkillResult Use(Creature attacker, Skill skill, long targetEntityId)
		{
			// Check target
			var mainTarget = attacker.Region.GetCreature(targetEntityId);
			if (mainTarget == null)
				return CombatSkillResult.InvalidTarget;

			// Check range
			var attackerPosition = attacker.GetPosition();
			var mainTargetPosition = mainTarget.GetPosition();

			if (!attackerPosition.InRange(mainTargetPosition, attacker.AttackRangeFor(mainTarget)))
				return CombatSkillResult.OutOfRange; 

			if (attacker.Region.Collisions.Any(attackerPosition, mainTargetPosition)) // Check collisions between position
				return CombatSkillResult.Okay;

			return UseWithoutRangeCheck(attacker, skill, targetEntityId, mainTarget);
		}

		/// <summary>
		/// Handles skill usage while ignoring range.
		/// </summary>
		/// <param name="attacker"></param>
		/// <param name="skill"></param>
		/// <param name="targetEntityId"></param>
		/// <returns></returns>
		public CombatSkillResult UseWithoutRangeCheck(Creature attacker, Skill skill, long targetEntityId, Creature mainTarget, SkillId interceptingSkillId = SkillId.None)
		{
			// Against Normal Attack
			Skill combatMastery = mainTarget.Skills.Get(SkillId.CombatMastery);
			if (interceptingSkillId == SkillId.None && combatMastery != null && (mainTarget.Skills.ActiveSkill == null || mainTarget.Skills.ActiveSkill == combatMastery || mainTarget.Skills.IsReady(SkillId.FinalHit)) && mainTarget.IsInBattleStance && mainTarget.Target == attacker && mainTarget.AttemptingAttack && (!mainTarget.IsStunned || mainTarget.IsKnockedDown))
			{
				if (mainTarget.CanTarget(attacker) && mainTarget.Can(Locks.Attack)) //TODO: Add Hit lock when available.
				{
					var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(combatMastery.Info.Id);
					if (skillHandler == null)
					{
						Log.Error("Smash.Use: Target's skill handler not found for '{0}'.", combatMastery.Info.Id);
						return CombatSkillResult.Okay;
					}
					((CombatMastery)skillHandler).UseWithoutRangeCheck(mainTarget, combatMastery, attacker.EntityId, attacker, SkillId.Smash);
					return CombatSkillResult.Okay;
				}
			}

			// Against Smash
			Skill smash = mainTarget.Skills.Get(SkillId.Smash);
			if (interceptingSkillId == SkillId.None && smash != null && mainTarget.Skills.IsReady(SkillId.Smash) && mainTarget.IsInBattleStance && mainTarget.Target == attacker && !mainTarget.IsStunned)
			{
				var attackerStunTime = CombatMastery.GetAttackerStun(attacker, attacker.RightHand, false);
				var mainTargetStunTime = CombatMastery.GetAttackerStun(mainTarget, mainTarget.Inventory.RightHand, false);
				var slowestStun = CombatMastery.GetAttackerStun(1, AttackSpeed.VerySlow, false);
				var additionalStun = slowestStun + (CombatMastery.GetAttackerStun(5, AttackSpeed.VeryFast, false) / 2); //Fastest stun divided by two so that the fastest stun doesn't always beat out the slowest stun.  The addition is so that the subtration (Ex. additionalStun - attackerStunTime) ends in the desired range.
				var formulaMultiplier = 320; //Multiplier to keep the result reasonable, found through trial and error?
				var formulaEqualizer = 50; //Balances the subtraction to keep the result in a reasonable range and balanced out no matter the order.
				double chances = ((((additionalStun - attackerStunTime) / slowestStun) * formulaMultiplier) - (((additionalStun - mainTargetStunTime) / slowestStun) * formulaMultiplier)) + formulaEqualizer; //Probability in percentage that you will not lose.
				chances = Math2.Clamp(0.0, 99.0, chances); //Cap the stun, just in case.

				if (((mainTarget.LastKnockedBackBy == attacker && mainTarget.KnockDownTime > attacker.KnockDownTime && mainTarget.KnockDownTime.AddMilliseconds(mainTargetStunTime) > DateTime.Now ||
					/*attackerStunTime > initialTargetStunTime && */
					!(attacker.LastKnockedBackBy == mainTarget && attacker.KnockDownTime > mainTarget.KnockDownTime && attacker.KnockDownTime.AddMilliseconds(attackerStunTime) > DateTime.Now))))
				{

					if (mainTarget.CanTarget(attacker) && mainTarget.Can(Locks.Attack)) //TODO: Add Hit lock when available.
					{
						var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(smash.Info.Id);
						if (skillHandler == null)
						{
							Log.Error("Smash.Use: Target's skill handler not found for '{0}'.", smash.Info.Id);
							return CombatSkillResult.Okay;
						}
						((Smash)skillHandler).UseWithoutRangeCheck(mainTarget, smash, attacker.EntityId, attacker, SkillId.Smash);
						return CombatSkillResult.Okay;
					}
				}
				else
				{
					interceptingSkillId = SkillId.Smash;
				}
			}

			// Stop movement
			attacker.StopMove();
			mainTarget.StopMove();

			// Get targets, incl. splash.
			// Splash happens from r5 onwards, but we'll base it on Var4,
			// which is the splash damage and first != 0 on r5.
			var targets = new HashSet<Creature>() { mainTarget };
			if (skill.RankData.Var4 != 0)
				targets.UnionWith(attacker.GetTargetableCreaturesInCone(mainTarget.GetPosition(), attacker.GetTotalSplashRadius(), attacker.GetTotalSplashAngle()));

			// Counter
			if (Counterattack.Handle(targets, attacker))
				return CombatSkillResult.Okay;

			// Prepare combat actions
			var aAction = new AttackerAction(CombatActionType.HardHit, attacker, targetEntityId);
			aAction.Set(AttackerOptions.Result | AttackerOptions.KnockBackHit2);
			aAction.Stun = StunTime;

			var cap = new CombatActionPack(attacker, skill.Info.Id);

			// Calculate damage
			var mainDamage = this.GetDamage(attacker, skill);

			foreach (var target in targets)
			{
				// Stop movement
				target.StopMove();

				TargetAction tAction;
				if (target == mainTarget)
				{
					if (interceptingSkillId == SkillId.Smash)
					{
						aAction.Options |= AttackerOptions.Result;
						tAction = new TargetAction(CombatActionType.CounteredHit, target, attacker, SkillId.Smash);

					}
					else
					{
						tAction = new TargetAction(CombatActionType.TakeHit, target, attacker, skill.Info.Id);
					}
				}
				else
				{
					tAction = new TargetAction(CombatActionType.TakeHit, target, attacker, skill.Info.Id);
				}

				tAction.Set(TargetOptions.Result | TargetOptions.Smash);

				cap.Add(tAction);
				if (target == mainTarget)
					cap.Add(aAction);

				// Damage
				var damage = mainDamage;

				// Elementals
				damage *= attacker.CalculateElementalDamageMultiplier(target);

				// Splash modifier
				if (target != mainTarget)
					damage *= (skill.RankData.Var4 / 100f);

				// Critical Hit
				var critChance = this.GetCritChance(attacker, target, skill);
				CriticalHit.Handle(attacker, critChance, ref damage, tAction);

				// Subtract target def/prot
				SkillHelper.HandleDefenseProtection(target, ref damage);

				// Conditions
				SkillHelper.HandleConditions(attacker, target, ref damage);

				// Mana Shield
				ManaShield.Handle(target, ref damage, tAction);

				// Heavy Stander
				HeavyStander.Handle(attacker, target, ref damage, tAction);

				// Apply damage
				if (damage > 0)
				{
					target.TakeDamage(tAction.Damage = damage, attacker);
					SkillHelper.HandleInjury(attacker, target, damage);
				}

				// Aggro
				if (target == mainTarget)
					target.Aggro(attacker);

				if (target.IsDead)
					tAction.Set(TargetOptions.FinishingHit | TargetOptions.Finished);

				// Set Stun/Knockback
				target.Stun = tAction.Stun = StunTime;
				target.Stability = Creature.MinStability;

				// Set knockbacked position
				attacker.Shove(target, KnockbackDistance);
			}

			// Response
			Send.SkillUseStun(attacker, skill.Info.Id, AfterUseStun, 1);

			// Update both weapons
			SkillHelper.UpdateWeapon(attacker, mainTarget, ProficiencyGainType.Melee, attacker.RightHand, attacker.LeftHand);

			// Action!
			cap.Handle();

			return CombatSkillResult.Okay;
		}

        /// <summary>
        /// Returns the raw damage to be done.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="skill"></param>
        /// <returns></returns>
        protected float GetDamage(Creature attacker, Skill skill)
		{
			var result = attacker.GetRndTotalDamage();
			result *= skill.RankData.Var1 / 100f;

			// +20% dmg for 2H
			if (attacker.RightHand != null && attacker.RightHand.Data.Type == ItemType.Weapon2H)
				result *= 1.20f;

			return result;
		}

		/// <summary>
		/// Returns the chance for a critical hit to happen.
		/// </summary>
		/// <param name="attacker"></param>
		/// <param name="target"></param>
		/// <param name="skill"></param>
		/// <returns></returns>
		protected float GetCritChance(Creature attacker, Creature target, Skill skill)
		{
			var result = attacker.GetTotalCritChance(target.Protection);

			// +5% crit for 2H
			if (attacker.RightHand != null && attacker.RightHand.Data.Type == ItemType.Weapon2H)
				result *= 1.05f;

			return result;
		}

		/// <summary>
		/// Training, called when someone attacks something.
		/// </summary>
		/// <param name="tAction"></param>
		public void OnCreatureAttackedByPlayer(TargetAction tAction)
		{
			// Only train if used skill was Smash
			if (tAction.AttackerSkillId != SkillId.Smash)
				return;

			// Get skill
			var attackerSkill = tAction.Attacker.Skills.Get(SkillId.Smash);
			if (attackerSkill == null) return; // Should be impossible.

			// Learning by attacking
			switch (attackerSkill.Info.Rank)
			{
				case SkillRank.RF:
				case SkillRank.RE:
					attackerSkill.Train(1); // Use the skill successfully.
					if (tAction.Has(TargetOptions.Critical)) attackerSkill.Train(2); // Critical Hit with Smash.
					if (tAction.Creature.IsDead) attackerSkill.Train(3); // Finishing blow with Smash.
					break;

				case SkillRank.RD:
				case SkillRank.RC:
				case SkillRank.RB:
				case SkillRank.RA:
				case SkillRank.R9:
				case SkillRank.R8:
				case SkillRank.R7:
					if (tAction.Has(TargetOptions.Critical) && tAction.Creature.IsDead)
						attackerSkill.Train(4); // Finishing blow with Critical Hit.
					goto case SkillRank.RF;

				case SkillRank.R6:
				case SkillRank.R5:
				case SkillRank.R4:
				case SkillRank.R3:
				case SkillRank.R2:
				case SkillRank.R1:
					if (tAction.Has(TargetOptions.Critical)) attackerSkill.Train(1); // Critical Hit with Smash.
					if (tAction.Creature.IsDead) attackerSkill.Train(2); // Finishing blow with Smash.
					if (tAction.Has(TargetOptions.Critical) && tAction.Creature.IsDead) attackerSkill.Train(3); // Finishing blow with Critical Hit.
					break;
			}
		}
	}
}

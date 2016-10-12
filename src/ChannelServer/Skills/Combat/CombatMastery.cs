﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aura.Channel.Skills.Base;
using Aura.Mabi.Const;
using Aura.Channel.World.Entities;
using Aura.Shared.Util;
using Aura.Data.Database;
using Aura.Channel.World;
using Aura.Channel.Skills.Life;
using Aura.Mabi;
using Aura.Channel.Skills.Magic;
using Aura.Channel.Network.Sending;

namespace Aura.Channel.Skills.Combat
{
	/// <summary>
	/// Combat Mastery
	/// </summary>
	/// <remarks>
	/// Normal attack for 99% of all races.
	/// </remarks>
	[Skill(SkillId.CombatMastery)]
	public class CombatMastery : ICombatSkill, IInitiableSkillHandler
	{
		/// <summary>
		/// Units an enemy is knocked back.
		/// </summary>
		private const int KnockBackDistance = 450;

		/// <summary>
		/// Subscribes skill to events needed for training.
		/// </summary>
		public void Init()
		{
			ChannelServer.Instance.Events.CreatureAttackedByPlayer += this.OnCreatureAttackedByPlayer;
		}

		/// <summary>
		/// Handles attack.
		/// </summary>
		/// <param name="attacker">The creature attacking.</param>
		/// <param name="skill">The skill being used.</param>
		/// <param name="targetEntityId">The entity id of the target.</param>
		/// <returns></returns>
		public CombatSkillResult Use(Creature attacker, Skill skill, long targetEntityId)
		{
			var mainTarget = attacker.Region.GetCreature(targetEntityId);
			if (mainTarget == null)
				return CombatSkillResult.Okay;

			var attackerPosition = attacker.GetPosition();
			var mainTargetPosition = mainTarget.GetPosition();

			if (!attackerPosition.InRange(mainTargetPosition, attacker.AttackRangeFor(mainTarget)))
				return CombatSkillResult.OutOfRange;

			if (attacker.Region.Collisions.Any(attackerPosition, mainTargetPosition)) // Check collisions between position
				return CombatSkillResult.Okay;

			return UseWithoutRangeCheck(attacker, skill, targetEntityId, mainTarget);
		}

		public CombatSkillResult UseWithoutRangeCheck(Creature attacker, Skill skill, long targetEntityId, Creature mainTarget, SkillId interceptingSkillId = SkillId.None)
		{
			if (mainTarget.Conditions.Has(ConditionsA.Invisible)) // Check visiblility (GM)
				return CombatSkillResult.Okay;

			//Against Smash
			Skill smash = mainTarget.Skills.Get(SkillId.Smash);
			if (interceptingSkillId == SkillId.None && smash != null && mainTarget.Skills.IsReady(SkillId.Smash))
				interceptingSkillId = SkillId.Smash;

			var rightWeapon = attacker.Inventory.RightHand;
			var leftWeapon = attacker.Inventory.LeftHand;
			var dualWield = (rightWeapon != null && leftWeapon != null && leftWeapon.Data.WeaponType != 0 && (leftWeapon.HasTag("/weapon/edged/") || leftWeapon.HasTag("/weapon/blunt/")));

			// Against Combat Mastery
			Skill combatMastery = mainTarget.Skills.Get(SkillId.CombatMastery);
			var simultaneousAttackStun = 0;
			if (interceptingSkillId == SkillId.None)
			{
				if (combatMastery != null && (mainTarget.Skills.ActiveSkill == null || mainTarget.Skills.ActiveSkill == combatMastery || mainTarget.Skills.IsReady(SkillId.FinalHit)) && mainTarget.IsInBattleStance && mainTarget.Target == attacker && mainTarget.AttemptingAttack && (!mainTarget.IsStunned || mainTarget.IsKnockedDown))
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
						!Math2.Probability(chances) && !(attacker.LastKnockedBackBy == mainTarget && attacker.KnockDownTime > mainTarget.KnockDownTime && attacker.KnockDownTime.AddMilliseconds(attackerStunTime) > DateTime.Now))))
					{
						if (!Math2.Probability(chances)) //Probability in percentage that it will be an interception instead of a double hit.  Always in favor of the faster attacker.
						{
							if (mainTarget.CanTarget(attacker) && mainTarget.Can(Locks.Attack)) //TODO: Add Hit lock when available.
							{
								var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(combatMastery.Info.Id);
								if (skillHandler == null)
								{
									Log.Error("CombatMastery.Use: Target's skill handler not found for '{0}'.", combatMastery.Info.Id);
									return CombatSkillResult.Okay;
								}
								((CombatMastery)skillHandler).UseWithoutRangeCheck(mainTarget, combatMastery, attacker.EntityId, attacker, SkillId.CombatMastery);
								return CombatSkillResult.Okay;
							}
						}
						else
						{
							interceptingSkillId = SkillId.CombatMastery;
							if (mainTarget.CanTarget(attacker) && mainTarget.Can(Locks.Attack)) //TODO: Add Hit lock when available.
							{
								var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(combatMastery.Info.Id);
								if (skillHandler == null)
								{
									Log.Error("CombatMastery.Use: Target's skill handler not found for '{0}'.", combatMastery.Info.Id);
								}
								else
								{
									((CombatMastery)skillHandler).UseWithoutRangeCheck(mainTarget, combatMastery, attacker.EntityId, attacker, SkillId.CombatMastery);
									simultaneousAttackStun = attacker.Stun;
									attacker.Stun = 0;
								}
							}
						}
					}
					else
					{
						if (Math2.Probability(chances)) //Probability in percentage that it will be an interception instead of a double hit.  Always in favor of the faster attacker.
						{
							interceptingSkillId = SkillId.CombatMastery;
						}
						else
						{
							interceptingSkillId = SkillId.CombatMastery;
							if (mainTarget.CanTarget(attacker) && mainTarget.Can(Locks.Attack)) //TODO: Add Hit lock when available.
							{
								var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(combatMastery.Info.Id);
								if (skillHandler == null)
								{
									Log.Error("CombatMastery.Use: Target's skill handler not found for '{0}'.", combatMastery.Info.Id);
								}
								else
								{
									((CombatMastery)skillHandler).UseWithoutRangeCheck(mainTarget, combatMastery, attacker.EntityId, attacker, SkillId.CombatMastery);
									simultaneousAttackStun = attacker.Stun;
									attacker.Stun = 0;
								}
							}
						}
					}
				}
			}

			attacker.StopMove();
			mainTarget.StopMove();

			// Get targets, incl. splash.
			var targets = new HashSet<Creature>() { mainTarget };
			targets.UnionWith(attacker.GetTargetableCreaturesInCone(mainTarget.GetPosition(), attacker.GetTotalSplashRadius(), attacker.GetTotalSplashAngle()));

			// Counter
			if (Counterattack.Handle(targets, attacker))
				return CombatSkillResult.Okay;

			var magazine = attacker.Inventory.Magazine;
			var maxHits = (byte)(attacker.IsDualWielding ? 2 : 1);
			int prevId = 0;

			for (byte i = 1; i <= maxHits; ++i)
			{
				var weapon = (i == 1 ? rightWeapon : leftWeapon);
				var weaponIsKnuckle = (weapon != null && weapon.Data.HasTag("/knuckle/"));

				AttackerAction aAction;

				if (interceptingSkillId == SkillId.Smash)
				{
					aAction = new AttackerAction(CombatActionType.SimultaneousHit, attacker, targetEntityId);
				}
				else if (interceptingSkillId == SkillId.CombatMastery)
				{
					aAction = new AttackerAction(CombatActionType.SimultaneousHit, attacker, targetEntityId);
				}
				else
				{
					aAction = new AttackerAction(CombatActionType.Attacker, attacker, targetEntityId);
				}

				aAction.Set(AttackerOptions.Result);

				if (attacker.IsDualWielding)
				{
					aAction.Set(AttackerOptions.DualWield);
					aAction.WeaponParameterType = (byte)(i == 1 ? 2 : 1);
				}

				var cap = new CombatActionPack(attacker, skill.Info.Id);
				if (interceptingSkillId != SkillId.Smash)
					cap.Add(aAction);
				cap.Hit = i;
				cap.Type = (attacker.IsDualWielding ? CombatActionPackType.TwinSwordAttack : CombatActionPackType.NormalAttack);
				cap.PrevId = prevId;
				prevId = cap.Id;

				var mainDamage = (i == 1 ? attacker.GetRndRightHandDamage() : attacker.GetRndLeftHandDamage());

				foreach (var target in targets)
				{
					if (target.IsDead)
						continue;

					target.StopMove();

					TargetAction tAction;

					if (target == mainTarget)
					{
						if (interceptingSkillId == SkillId.Smash)
						{
							tAction = new TargetAction(CombatActionType.CounteredHit, target, attacker, SkillId.Smash);
						}
						else if (interceptingSkillId == SkillId.CombatMastery)
						{
							tAction = new TargetAction(CombatActionType.CounteredHit, target, attacker, target.Skills.IsReady(SkillId.FinalHit) ? SkillId.FinalHit : SkillId.CombatMastery);
						}
						else
						{
							tAction = new TargetAction(CombatActionType.TakeHit, target, attacker, target.Skills.IsReady(SkillId.FinalHit) ? SkillId.FinalHit : SkillId.CombatMastery);
						}
					}
					else
					{
						tAction = new TargetAction(CombatActionType.TakeHit, target, attacker, target.Skills.IsReady(SkillId.FinalHit) ? SkillId.FinalHit : SkillId.CombatMastery);
					}

					tAction.Set(TargetOptions.Result);

					cap.Add(tAction);
					if (target == mainTarget && interceptingSkillId == SkillId.Smash)
						cap.Add(aAction);


					// Base damage
					var damage = mainDamage;

					// Elementals
					damage *= attacker.CalculateElementalDamageMultiplier(target);

					// Splash modifier
					if (target != mainTarget)
						damage *= attacker.GetSplashDamage(weapon);

					// Critical Hit
					var critChance = (i == 1 ? attacker.GetRightCritChance(target.Protection) : attacker.GetLeftCritChance(target.Protection));
					CriticalHit.Handle(attacker, critChance, ref damage, tAction);

					// Subtract target def/prot
					SkillHelper.HandleDefenseProtection(target, ref damage);

					// Defense
					Defense.Handle(aAction, tAction, ref damage);

					// Mana Shield
					ManaShield.Handle(target, ref damage, tAction);

					// Heavy Stander
					// Can only happen on the first hit
					var pinged = (cap.Hit == 1 && HeavyStander.Handle(attacker, target, ref damage, tAction));

					// Deal with it!
					if (damage > 0)
					{
						target.TakeDamage(tAction.Damage = damage, attacker);
						SkillHelper.HandleInjury(attacker, target, damage);
					}

					// Knock down on deadly
					if (target.Conditions.Has(ConditionsA.Deadly))
					{
						tAction.Set(TargetOptions.KnockDown);
						tAction.Stun = GetTargetStun(attacker, weapon, tAction.IsKnockBack);
					}

					// Aggro
					if (target == mainTarget)
						target.Aggro(attacker);

					// Evaluate caused damage
					if (!target.IsDead)
					{
						if (tAction.SkillId != SkillId.Defense)
						{
							target.Stability -= this.GetStabilityReduction(attacker, weapon) / maxHits;

							// React normal for CombatMastery, knock down if 
							// FH and not dual wield, don't knock at all if dual.
							if (skill.Info.Id != SkillId.FinalHit)
							{
								// Originally we thought you knock enemies back, unless it's a critical
								// hit, but apparently you knock *down* under normal circumstances.
								// More research to be done.
								if (target.IsUnstable && target.Is(RaceStands.KnockBackable))
									//tAction.Set(tAction.Has(TargetOptions.Critical) ? TargetOptions.KnockDown : TargetOptions.KnockBack);
									tAction.Set(TargetOptions.KnockDown);
							}
							else if (!attacker.IsDualWielding && !weaponIsKnuckle && target.Is(RaceStands.KnockBackable))
							{
								target.Stability = Creature.MinStability;
								tAction.Set(TargetOptions.KnockDown);
							}
						}
					}
					else
					{
						tAction.Set(TargetOptions.FinishingKnockDown);
					}

					// React to knock back
					if (tAction.IsKnockBack)
					{
						attacker.Shove(target, KnockBackDistance);
						if (target == mainTarget)
							aAction.Set(AttackerOptions.KnockBackHit2);
					}

					// Set stun time if not defended, Defense handles the stun
					// in case the target used it.
					if (tAction.SkillId != SkillId.Defense)
					{
						if (target == mainTarget)
							aAction.Stun = GetAttackerStun(attacker, weapon, tAction.IsKnockBack && skill.Info.Id != SkillId.FinalHit);
						tAction.Stun = GetTargetStun(attacker, weapon, tAction.IsKnockBack);
					}

					if (target == mainTarget)
					{
						// Set increased stun if target pinged
						if (pinged)
							aAction.Stun = GetAttackerStun(attacker, weapon, true);

						// Second hit doubles stun time for normal hits
						if (cap.Hit == 2 && !tAction.IsKnockBack && !pinged)
							aAction.Stun *= 2;

						// Update current weapon
						SkillHelper.UpdateWeapon(attacker, target, ProficiencyGainType.Melee, weapon);

						// Consume stamina for weapon
						var staminaUsage = (rightWeapon != null && rightWeapon.Data.StaminaUsage != 0 ? rightWeapon.Data.StaminaUsage : 0.7f) + (dualWield ? leftWeapon.Data.StaminaUsage : 0f);
						if (attacker.Stamina < staminaUsage)
							Send.Notice(attacker, Localization.Get("Your stamina is too low to fight properly!"));
						attacker.Stamina -= staminaUsage;

						// No second hit if defended, pinged, or knocked back
						if (tAction.IsKnockBack || tAction.SkillId == SkillId.Defense || pinged)
						{
							// Set to 1 to prevent second run
							maxHits = 1;

							// Remove dual wield option if last hit doesn't come from
							// the second weapon. If this isn't done, the client shows
							// the second hit.
							if (cap.Hit != 2)
								aAction.Options &= ~AttackerOptions.DualWield;
						}
					}
				}

				// Handle
				cap.Handle();
			}

			attacker.AttemptingAttack = false;
			return CombatSkillResult.Okay;
		}

		/// <summary>
		/// Returns stun time for the attacker.
		/// </summary>
		/// <param name="weapon"></param>
		/// <param name="knockback"></param>
		/// <returns></returns>
		public static short GetAttackerStun(Creature creature, Item weapon, bool knockback)
		{
			var count = weapon != null ? weapon.Info.KnockCount + 1 : creature.RaceData.KnockCount + 1;
			var speed = weapon != null ? (AttackSpeed)weapon.Data.AttackSpeed : (AttackSpeed)creature.RaceData.AttackSpeed;

			return GetAttackerStun(count, speed, knockback);
		}

		/// <summary>
		/// Returns stun time for the attacker.
		/// </summary>
		/// <param name="count"></param>
		/// <param name="speed"></param>
		/// <param name="knockback"></param>
		/// <returns></returns>
		public static short GetAttackerStun(int count, AttackSpeed speed, bool knockback)
		{
			if (knockback)
				return 2500;

			// Speeds commented with "?" weren't logged, but taken from the weapon data.
			// Stun *seems* to always be the same, needs confirmation. Except for 1-hit,
			// which is always knock-back.

			switch (count)
			{
				case 1:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 2500;
					}
					break;

				case 2:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1000;
						case AttackSpeed.Slow: return 800;
						case AttackSpeed.Normal: return 600; // ?
						case AttackSpeed.Fast: return 520; // ?
						case AttackSpeed.VeryFast: return 450; // ?
					}
					break;

				case 3:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1000;
						case AttackSpeed.Slow: return 800;
						case AttackSpeed.Normal: return 600;
						case AttackSpeed.Fast: return 520;
						case AttackSpeed.VeryFast: return 450; // ?
					}
					break;

				case 4:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1000; // ?
						case AttackSpeed.Slow: return 800; // ?
						case AttackSpeed.Normal: return 600; // ?
						case AttackSpeed.Fast: return 520; // ?
						case AttackSpeed.VeryFast: return 450; // ?
					}
					break;

				case 5:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1000; // ?
						case AttackSpeed.Slow: return 800; // ?
						case AttackSpeed.Normal: return 600; // ?
						case AttackSpeed.Fast: return 520; // ?
						case AttackSpeed.VeryFast: return 450;
					}
					break;
			}

			Log.Unimplemented("GetAttackerStun: Combination {0} {1} Hit", speed, count);

			return 600;
		}

		/// <summary>
		/// Returns stun time for the target.
		/// </summary>
		/// <param name="weapon"></param>
		/// <param name="knockback"></param>
		/// <returns></returns>
		public static short GetTargetStun(Creature creature, Item weapon, bool knockback)
		{
			var count = weapon != null ? weapon.Info.KnockCount + 1 : creature.RaceData.KnockCount + 1;
			var speed = weapon != null ? (AttackSpeed)weapon.Data.AttackSpeed : (AttackSpeed)creature.RaceData.AttackSpeed;

			return GetTargetStun(count, speed, knockback);
		}

		/// <summary>
		/// Returns stun time for the target.
		/// </summary>
		/// <param name="count"></param>
		/// <param name="speed"></param>
		/// <param name="knockback"></param>
		/// <returns></returns>
		public static short GetTargetStun(int count, AttackSpeed speed, bool knockback)
		{
			if (knockback)
				return 3000;

			// Speeds commented with "?" weren't logged, but taken from the weapon data.

			switch (count)
			{
				case 1:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 3000;
					}
					break;

				case 2:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 3000;
						case AttackSpeed.Slow: return 2800;
						case AttackSpeed.Normal: return 2600; // ?
						case AttackSpeed.Fast: return 2400; // ?
						case AttackSpeed.VeryFast: return 2200; // ?
					}
					break;

				case 3:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 2200;
						case AttackSpeed.Slow: return 2100;
						case AttackSpeed.Normal: return 2000;
						case AttackSpeed.Fast: return 1700;
						case AttackSpeed.VeryFast: return 1500; // ?
					}
					break;

				case 4:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1900; // ?
						case AttackSpeed.Slow: return 1800; // ?
						case AttackSpeed.Normal: return 1700; // ?
						case AttackSpeed.Fast: return 1500; // ?
						case AttackSpeed.VeryFast: return 1300; // ?
					}
					break;

				case 5:
					switch (speed)
					{
						case AttackSpeed.VerySlow: return 1700; // ?
						case AttackSpeed.Slow: return 1600; // ?
						case AttackSpeed.Normal: return 1500; // ?
						case AttackSpeed.Fast: return 1400; // ?
						case AttackSpeed.VeryFast: return 1200;
					}
					break;
			}

			Log.Unimplemented("GetTargetStun: Combination {0} {1} Hit", speed, count);

			return 2000;
		}

		/// <summary>
		/// Returns stability reduction for creature and weapon.
		/// </summary>
		/// <remarks>
		/// http://wiki.mabinogiworld.com/view/Knock_down_gauge#Knockdown_Timer_Rates
		/// </remarks>
		/// <param name="weapon"></param>
		/// <returns></returns>
		public float GetStabilityReduction(Creature creature, Item weapon)
		{
			var count = weapon != null ? weapon.Info.KnockCount + 1 : creature.RaceData.KnockCount + 1;
			var speed = weapon != null ? (AttackSpeed)weapon.Data.AttackSpeed : (AttackSpeed)creature.RaceData.AttackSpeed;

			// All values have been taken from the weapons data, the values in
			// comments were estimates, mainly based on logs.

			switch (count)
			{
				default:
				case 1:
					return 105;

				case 2:
					switch (speed)
					{
						default:
						case AttackSpeed.VerySlow: return 67; // 70
						case AttackSpeed.Slow: return 65; // 68
						case AttackSpeed.Normal: return 65; // 68
						case AttackSpeed.Fast: return 65; // 68
						case AttackSpeed.VeryFast: return 65;
					}

				case 3:
					switch (speed)
					{
						default:
						case AttackSpeed.VerySlow: return 55; // 60
						case AttackSpeed.Slow: return 52; // 56
						case AttackSpeed.Normal: return 50; // 53
						case AttackSpeed.Fast: return 49; // 50
						case AttackSpeed.VeryFast: return 48;
					}

				case 4:
					switch (speed)
					{
						default:
						case AttackSpeed.VerySlow: return 42;
						case AttackSpeed.Slow: return 40;
						case AttackSpeed.Normal: return 39;
						case AttackSpeed.Fast: return 36;
						case AttackSpeed.VeryFast: return 37;
					}

				case 5:
					switch (speed)
					{
						default:
						case AttackSpeed.VerySlow: return 36;
						case AttackSpeed.Slow: return 33;
						case AttackSpeed.Normal: return 31.5f;
						case AttackSpeed.Fast: return 30; // 40
						case AttackSpeed.VeryFast: return 29.5f; // 35
					}
			}
		}

		/// <summary>
		/// Training, called when someone attacks something.
		/// </summary>
		/// <param name="action"></param>
		public void OnCreatureAttackedByPlayer(TargetAction action)
		{
			// Get skill
			var attackerSkill = action.Attacker.Skills.Get(SkillId.CombatMastery);
			if (attackerSkill == null) return; // Should be impossible.
			var targetSkill = action.Creature.Skills.Get(SkillId.CombatMastery);
			if (targetSkill == null) return; // Should be impossible.

			var rating = action.Attacker.GetPowerRating(action.Creature);
			var targetRating = action.Creature.GetPowerRating(action.Attacker);

			// TODO: Check for multiple hits...?

			// Learning by attacking
			switch (attackerSkill.Info.Rank)
			{
				case SkillRank.Novice:
					attackerSkill.Train(1); // Attack Anything.
					break;

				case SkillRank.RF:
					attackerSkill.Train(1); // Attack anything.
					attackerSkill.Train(2); // Attack an enemy.
					if (action.IsKnockBack) attackerSkill.Train(3); // Knock down an enemy with multiple hits.
					if (action.Creature.IsDead) attackerSkill.Train(4); // Kill an enemy.
					break;

				case SkillRank.RE:
					if (rating == PowerRating.Normal) attackerSkill.Train(3); // Attack a same level enemy.

					if (action.IsKnockBack)
					{
						attackerSkill.Train(1); // Knock down an enemy with multiple hits.
						if (rating == PowerRating.Normal) attackerSkill.Train(4); // Knockdown a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(7); // Knockdown a strong enemy.
					}

					if (action.Creature.IsDead)
					{
						attackerSkill.Train(2); // Kill an enemy.
						if (rating == PowerRating.Normal) attackerSkill.Train(6); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(8); // Kill a strong enemy.
					}

					break;

				case SkillRank.RD:
					attackerSkill.Train(1); // Attack an enemy.
					if (rating == PowerRating.Normal) attackerSkill.Train(4); // Attack a same level enemy.

					if (action.IsKnockBack)
					{
						attackerSkill.Train(2); // Knock down an enemy with multiple hits.
						if (rating == PowerRating.Normal) attackerSkill.Train(5); // Knockdown a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(7); // Knockdown a strong enemy.
					}

					if (action.Creature.IsDead)
					{
						attackerSkill.Train(3); // Kill an enemy.
						if (rating == PowerRating.Normal) attackerSkill.Train(6); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(8); // Kill a strong enemy.
					}

					break;

				case SkillRank.RC:
				case SkillRank.RB:
					if (rating == PowerRating.Normal) attackerSkill.Train(1); // Attack a same level enemy.

					if (action.IsKnockBack)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(2); // Knockdown a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(4); // Knockdown a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(6); // Knockdown an awful level enemy.
					}

					if (action.Creature.IsDead)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(3); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(5); // Kill a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(7); // Kill an awful level enemy.
					}

					break;

				case SkillRank.RA:
				case SkillRank.R9:
					if (action.IsKnockBack)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(1); // Knockdown a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(3); // Knockdown a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(5); // Knockdown an awful level enemy.
					}

					if (action.Creature.IsDead)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(2); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(4); // Kill a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(6); // Kill an awful level enemy.
					}

					break;

				case SkillRank.R8:
					if (action.IsKnockBack)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(1); // Knockdown a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(3); // Knockdown a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(5); // Knockdown an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(7); // Knockdown a boss level enemy.
					}

					if (action.Creature.IsDead)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(2); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(4); // Kill a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(6); // Kill an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(8); // Kill a boss level enemy.
					}

					break;

				case SkillRank.R7:
					if (action.IsKnockBack)
					{
						if (rating == PowerRating.Strong) attackerSkill.Train(2); // Knockdown a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(4); // Knockdown an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(6); // Knockdown a boss level enemy.
					}

					if (action.Creature.IsDead)
					{
						if (rating == PowerRating.Normal) attackerSkill.Train(1); // Kill a same level enemy.
						if (rating == PowerRating.Strong) attackerSkill.Train(3); // Kill a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(5); // Kill an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(7); // Kill a boss level enemy.
					}

					break;

				case SkillRank.R6:
				case SkillRank.R5:
				case SkillRank.R4:
				case SkillRank.R3:
				case SkillRank.R2:
				case SkillRank.R1:
					if (action.IsKnockBack)
					{
						if (rating == PowerRating.Strong) attackerSkill.Train(1); // Knockdown a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(3); // Knockdown an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(5); // Knockdown a boss level enemy.
					}

					if (action.Creature.IsDead)
					{
						if (rating == PowerRating.Strong) attackerSkill.Train(2); // Kill a strong level enemy.
						if (rating == PowerRating.Awful) attackerSkill.Train(4); // Kill an awful level enemy.
						if (rating == PowerRating.Boss) attackerSkill.Train(6); // Kill a boss level enemy.
					}

					break;
			}

			// Learning by being attacked
			switch (targetSkill.Info.Rank)
			{
				case SkillRank.RF:
					if (action.IsKnockBack) targetSkill.Train(5); // Learn something by falling down.
					if (action.Creature.IsDead) targetSkill.Train(6); // Learn through losing.
					break;

				case SkillRank.RE:
					if (action.IsKnockBack) targetSkill.Train(5); // Get knocked down. 
					break;

				case SkillRank.RD:
					if (targetRating == PowerRating.Strong) targetSkill.Train(9); // Get hit by an awful level enemy.
					break;
			}
		}
	}
}

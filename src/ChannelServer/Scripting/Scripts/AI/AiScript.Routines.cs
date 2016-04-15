// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Channel.Network.Sending;
using Aura.Channel.Skills;
using Aura.Channel.Skills.Base;
using Aura.Channel.Skills.Combat;
using Aura.Channel.Skills.Life;
using Aura.Channel.World;
using Aura.Channel.World.Entities;
using Aura.Data;
using Aura.Mabi;
using Aura.Mabi.Const;
using Aura.Shared.Network;
using Aura.Shared.Scripting.Scripts;
using Aura.Shared.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Aura.Channel.Scripting.Scripts.Ai
{
	public abstract partial class AiScript : IScript, IDisposable
	{
		/// <summary>
		/// Makes creature say something in public chat.
		/// </summary>
		/// <param name="msg"></param>
		/// <returns></returns>
		protected IEnumerable Say(string msg)
		{
			if (!string.IsNullOrWhiteSpace(msg))
				Send.Chat(this.Creature, msg);

			yield break;
		}

		/// <summary>
		/// Makes creature say one of the messages in public chat.
		/// </summary>
		/// <param name="msgs"></param>
		/// <returns></returns>
		protected IEnumerable Say(params string[] msgs)
		{
			if (msgs == null || msgs.Length == 0)
				yield break;

			var msg = msgs[this.Random(msgs.Length)];
			if (!string.IsNullOrWhiteSpace(msg))
				Send.Chat(this.Creature, msg);

			yield break;
		}

		/// <summary>
		/// Makes creature say a random phrase in public chat.
		/// </summary>
		/// <returns></returns>
		protected IEnumerable SayRandomPhrase()
		{
			if (this.Phrases.Count > 0)
				Send.Chat(this.Creature, this.Phrases[this.Random(this.Phrases.Count)]);
			yield break;
		}

		/// <summary>
		/// Makes AI wait for a random amount of ms, between min and max.
		/// </summary>
		/// <param name="min"></param>
		/// <param name="max"></param>
		/// <returns></returns>
		protected IEnumerable Wait(int min, int max = 0)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			if (max < min)
				max = min;

			var duration = (min == max ? min : this.Random(min, max + 1));
			var target = _timestamp + duration;

			while (_timestamp < target)
			{
				yield return true;
			}
		}

		/// <summary>
		/// Makes creature walk to a random position in range.
		/// </summary>
		/// <param name="minDistance"></param>
		/// <param name="maxDistance"></param>
		/// <param name="walk"></param>
		/// <returns></returns>
		protected IEnumerable Wander(int minDistance = 100, int maxDistance = 600, bool walk = true)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			if (maxDistance < minDistance)
				maxDistance = minDistance;

			var rnd = RandomProvider.Get();
			var pos = this.Creature.GetPosition();
			var destination = pos.GetRandomInRange(minDistance, maxDistance, rnd);

			// Make sure NPCs don't wander off
			var npc = this.Creature as NPC;
			if (npc != null && destination.GetDistance(npc.SpawnLocation.Position) > _maxDistanceFromSpawn)
				destination = pos.GetRelative(npc.SpawnLocation.Position, (minDistance + maxDistance) / 2);

			foreach (var action in this.MoveTo(destination, walk))
				yield return action;
		}

		/// <summary>
		/// Runs action till it's done or the timeout is reached.
		/// </summary>
		/// <param name="timeout"></param>
		/// <param name="action"></param>
		/// <returns></returns>
		protected IEnumerable Timeout(double timeout, IEnumerable action)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			timeout += _timestamp;

			foreach (var a in action)
			{
				if (_timestamp >= timeout)
					yield break;
				yield return true;
			}
		}

		/// <summary>
		/// Creature runs to destination.
		/// </summary>
		/// <param name="destination"></param>
		/// <returns></returns>
		protected IEnumerable RunTo(Position destination)
		{
			return this.MoveTo(destination, false);
		}

		/// <summary>
		/// Creature walks to destination.
		/// </summary>
		/// <param name="destination"></param>
		/// <returns></returns>
		protected IEnumerable WalkTo(Position destination)
		{
			return this.MoveTo(destination, true);
		}

		/// <summary>
		/// Creature moves to destination.
		/// </summary>
		/// <param name="destination"></param>
		/// <param name="walk"></param>
		/// <returns></returns>
		protected IEnumerable MoveTo(Position destination, bool walk)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			var pos = this.Creature.GetPosition();

			// Check for collision
			Position intersection;
			if (this.Creature.Region.Collisions.Find(pos, destination, out intersection))
			{
				destination = pos.GetRelative(intersection, -100);

				// If new destination is invalid as well don't move at all
				if (this.Creature.Region.Collisions.Any(pos, destination))
					destination = pos;
			}

			this.Creature.Move(destination, walk);

			var time = this.Creature.MoveDuration * 1000;
			var walkTime = _timestamp + time;

			do
			{
				// Yield at least once, even if it took 0 time,
				// to avoid unexpected problems, like infinite outer loops,
				// because an action expected the walk to yield at least once.
				yield return true;
			}
			while (_timestamp < walkTime);
		}

		/// <summary>
		/// Creature circles around target.
		/// </summary>
		/// <param name="radius"></param>
		/// <param name="timeMin"></param>
		/// <param name="timeMax"></param>
		/// <param name="walk"></param>
		/// <returns></returns>
		protected IEnumerable Circle(int radius, int timeMin = 1000, int timeMax = 5000, bool walk = true)
		{
			return this.Circle(radius, timeMin, timeMax, this.Random() < 50, walk);
		}

		/// <summary>
		/// Creature circles around target.
		/// </summary>
		/// <param name="radius"></param>
		/// <param name="timeMin"></param>
		/// <param name="timeMax"></param>
		/// <param name="clockwise"></param>
		/// <param name="walk"></param>
		/// <returns></returns>
		protected IEnumerable Circle(int radius, int timeMin, int timeMax, bool clockwise, bool walk)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			if (timeMin < 500)
				timeMin = 500;
			if (timeMax < timeMin)
				timeMax = timeMin;

			var time = (timeMin == timeMax ? timeMin : this.Random(timeMin, timeMax + 1));
			var until = _timestamp + time;

			for (int i = 0; _timestamp < until || i == 0; ++i)
			{
				// Stop if target vanished somehow
				if (this.Creature.Target == null)
					yield break;

				var targetPos = this.Creature.Target.GetPosition();
				var pos = this.Creature.GetPosition();

				var deltaX = pos.X - targetPos.X;
				var deltaY = pos.Y - targetPos.Y;
				var angle = Math.Atan2(deltaY, deltaX) + (Math.PI / 8 * 2) * (clockwise ? -1 : 1);
				var x = targetPos.X + (Math.Cos(angle) * radius);
				var y = targetPos.Y + (Math.Sin(angle) * radius);

				foreach (var action in this.MoveTo(new Position((int)x, (int)y), walk))
					yield return action;
			}
		}

		/// <summary>
		/// Creature follows its target.
		/// </summary>
		/// <param name="maxDistance"></param>
		/// <param name="walk"></param>
		/// <param name="timeout"></param>
		/// <returns></returns>
		protected IEnumerable Follow(int maxDistance, bool walk = false, int timeout = 5000)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			var until = _timestamp + Math.Max(0, timeout);

			while (_timestamp < until)
			{
				// Stop if target vanished somehow
				if (this.Creature.Target == null)
					yield break;

				var pos = this.Creature.GetPosition();
				var targetPos = this.Creature.Target.GetPosition();

				if (!pos.InRange(targetPos, maxDistance))
				{
					// Walk up to distance-50 (a buffer so it really walks into range)
					this.ExecuteOnce(this.MoveTo(pos.GetRelative(targetPos, -maxDistance + 50), walk));
				}

				yield return true;
			}
		}

		/// <summary>
		/// Creature tries to get away from target.
		/// </summary>
		/// <param name="minDistance"></param>
		/// <param name="walk"></param>
		/// <param name="timeout"></param>
		/// <returns></returns>
		protected IEnumerable KeepDistance(int minDistance, bool walk = false, int timeout = 5000)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			var until = _timestamp + Math.Max(0, timeout);

			while (_timestamp < until)
			{
				var pos = this.Creature.GetPosition();
				var targetPos = this.Creature.Target.GetPosition();

				if (pos.InRange(targetPos, minDistance))
				{
					// The position to move to is on the line between pos and targetPos,
					// -distance from target to creature, resulting in a position
					// "behind" the creature.
					this.ExecuteOnce(this.MoveTo(pos.GetRelative(targetPos, -(minDistance + 50)), walk));
				}

				yield return true;
			}
		}

		/// <summary>
		/// Attacks target creature "KnockCount" times.
		/// </summary>
		/// <returns></returns>
		protected IEnumerable Attack()
		{
			var count = 1 + (this.Creature.Inventory.RightHand != null ? this.Creature.Inventory.RightHand.Info.KnockCount : this.Creature.RaceData.KnockCount);
			return this.Attack(count);
		}

		/// <summary>
		/// Attacks target creature x times.
		/// </summary>
		/// <param name="count"></param>
		/// <param name="timeout"></param>
		/// <returns></returns>
		protected IEnumerable Attack(int count, int timeout = 300000)
		{
			if (this.Creature.Target == null)
			{
				this.Reset();
				yield break;
			}

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			timeout = Math2.Clamp(0, 300000, timeout);
			var until = _timestamp + timeout;

			// Each successful hit counts, attack until count or timeout is reached.
			for (int i = 0; ; )
			{
				// Get skill
				var skill = this.Creature.Skills.ActiveSkill;
				if (skill == null && (skill = this.Creature.Skills.Get(SkillId.CombatMastery)) == null)
				{
					Log.Warning("AI.Attack: Creature '{0}' doesn't have Combat Mastery.", this.Creature.RaceId);
					yield break;
				}

				// Get skill handler
				var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(skill.Info.Id);
				if (skillHandler == null)
				{
					Log.Error("AI.Attack: Skill handler not found for '{0}'.", skill.Info.Id);
					yield break;
				}

				// Stop timeout was reached
				if (_timestamp >= until)
					break;

				// Stop if target vanished somehow
				if (this.Creature.Target == null)
					yield break;

				// Attack
				var result = skillHandler.Use(this.Creature, skill, this.Creature.Target.EntityId);
				if (result == CombatSkillResult.Okay)
				{
					// Stop when max attack count is reached
					if (++i >= count)
						break;

					yield return true;
				}
				else if (result == CombatSkillResult.OutOfRange)
				{
					// Run to target if out of range
					var pos = this.Creature.GetPosition();
					var targetPos = this.Creature.Target.GetPosition();

					//var attackRange = this.Creature.AttackRangeFor(this.Creature.Target);
					//this.ExecuteOnce(this.RunTo(pos.GetRelative(targetPos, -attackRange + 50)));
					this.ExecuteOnce(this.RunTo(targetPos));

					yield return true;
				}
				else
				{
					Log.Error("AI.Attack: Unhandled combat skill result ({0}).", result);
					yield break;
				}
			}

			// Complete is called automatically from OnUsedSkill
		}

		/// <summary>
		/// Attacks target with a ranged attack.
		/// </summary>
		/// <param name="timeout"></param>
		/// <returns></returns>
		protected IEnumerable RangedAttack(int timeout = 5000)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			var target = this.Creature.Target;

			// Check active skill
			var activeSkill = this.Creature.Skills.ActiveSkill;
			if (activeSkill != null)
			{
				if (activeSkill.Data.Type != SkillType.RangedCombat)
				{
					Log.Warning("AI.RangedAttack: Active skill is no ranged skill.", this.Creature.RaceId);
					yield break;
				}
			}
			else
			{
				// Get skill
				activeSkill = this.Creature.Skills.Get(SkillId.RangedAttack);
				if (activeSkill == null)
				{
					Log.Warning("AI.RangedAttack: Creature '{0}' doesn't have RangedAttack.", this.Creature.RaceId);
					yield break;
				}

				// Get handler
				var rangedHandler = ChannelServer.Instance.SkillManager.GetHandler<RangedAttack>(activeSkill.Info.Id);

				// Start loading
				this.SharpMind(activeSkill.Info.Id, SharpMindStatus.Loading);

				// Prepare skill
				rangedHandler.Prepare(this.Creature, activeSkill, null);

				this.Creature.Skills.ActiveSkill = activeSkill;
				activeSkill.State = SkillState.Prepared;

				// Wait for loading to be done
				foreach (var action in this.Wait(activeSkill.RankData.LoadTime))
					yield return action;

				// Call ready
				rangedHandler.Ready(this.Creature, activeSkill, null);
				activeSkill.State = SkillState.Ready;

				// Done loading
				this.SharpMind(activeSkill.Info.Id, SharpMindStatus.Loaded);
			}

			// Get combat handler for active skill
			var combatHandler = ChannelServer.Instance.SkillManager.GetHandler<ICombatSkill>(activeSkill.Info.Id);

			// Start aiming
			this.Creature.AimMeter.Start(target.EntityId);

			// Wait till aim is 99% or timeout is reached
			var until = _timestamp + Math.Max(0, timeout);
			var aim = 0.0;
			while (_timestamp < until && (aim = this.Creature.AimMeter.GetAimChance(target)) < 90)
				yield return true;

			// Cancel if 90 aim weren't reached
			if (aim < 90)
			{
				this.SharpMind(activeSkill.Info.Id, SharpMindStatus.Cancelling);
				this.Creature.Skills.CancelActiveSkill();
				this.Creature.AimMeter.Stop();
				yield break;
			}

			// Attack
			combatHandler.Use(this.Creature, activeSkill, target.EntityId);
			activeSkill.State = SkillState.Completed;

			// Complete is called automatically from OnUsedSkill
		}

		/// <summary>
		/// Attacks with the given skill, charging it first, if it doesn't
		/// have the given amount of stacks yet. Attacks until all stacks
		/// have been used, or timeout is reached.
		/// </summary>
		/// <param name="skillId"></param>
		/// <param name="stacks"></param>
		/// <param name="timeout"></param>
		/// <returns></returns>
		protected IEnumerable StackAttack(SkillId skillId, int stacks = 1, int timeout = 30000)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			var target = this.Creature.Target;
			var until = _timestamp + Math.Max(0, timeout);

			// Get handler
			var prepareHandler = ChannelServer.Instance.SkillManager.GetHandler<IPreparable>(skillId);
			var readyHandler = prepareHandler as IReadyable;
			var combatHandler = prepareHandler as ICombatSkill;

			if (prepareHandler == null || readyHandler == null || combatHandler == null)
			{
				Log.Warning("AI.StackAttack: {0}'s handler doesn't exist, or doesn't implement the necessary interfaces.", skillId);
				yield break;
			}

			// Cancel active skill if it's not the one we want
			var skill = this.Creature.Skills.ActiveSkill;
			if (skill != null && skill.Info.Id != skillId)
			{
				foreach (var action in this.CancelSkill())
					yield return action;
			}

			// Get skill if we don't have one yet
			if (skill == null)
			{
				// Get skill
				skill = this.Creature.Skills.Get(skillId);
				if (skill == null)
				{
					Log.Warning("AI.StackAttack: Creature '{0}' doesn't have {1}.", this.Creature.RaceId, skillId);
					yield break;
				}
			}

			// Stack up
			stacks = Math2.Clamp(1, skill.RankData.StackMax, stacks);
			while (skill.Stacks < stacks)
			{
				// Start loading
				this.SharpMind(skill.Info.Id, SharpMindStatus.Loading);

				// Prepare skill
				prepareHandler.Prepare(this.Creature, skill, null);

				this.Creature.Skills.ActiveSkill = skill;
				skill.State = SkillState.Prepared;

				// Wait for loading to be done
				foreach (var action in this.Wait(skill.RankData.LoadTime))
					yield return action;

				// Call ready
				readyHandler.Ready(this.Creature, skill, null);
				skill.State = SkillState.Ready;

				// Done loading
				this.SharpMind(skill.Info.Id, SharpMindStatus.Loaded);
			}

			// Small delay
			foreach (var action in this.Wait(1000, 2000))
				yield return action;

			// Attack
			while (skill.Stacks > 0)
			{
				if (_timestamp >= until)
					break;

				combatHandler.Use(this.Creature, skill, target.EntityId);
				yield return true;
			}

			// Cancel skill if there are left over stacks
			if (skill.Stacks != 0)
			{
				foreach (var action in this.CancelSkill())
					yield return action;
			}
		}

		/// <summary>
		/// Makes creature prepare given skill.
		/// </summary>
		/// <param name="skillId"></param>
		/// <returns></returns>
		protected IEnumerable PrepareSkill(SkillId skillId)
		{
			return this.PrepareSkill(skillId, 1);
		}

		/// <summary>
		/// Makes creature prepare given skill.
		/// </summary>
		/// <param name="skillId"></param>
		/// <returns></returns>
		protected IEnumerable PrepareSkill(SkillId skillId, int stacks)
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			// Get skill
			var skill = this.Creature.Skills.Get(skillId);
			if (skill == null)
			{
				// The AIs are designed to work with multiple races,
				// even if they might not possess certain skills.
				// We don't need a warning if they don't have the skill,
				// they simply shouldn't do anything in that case.

				//Log.Warning("AI.PrepareSkill: AI '{0}' tried to prepare skill '{2}', that its creature '{1}' doesn't have.", this.GetType().Name, this.Creature.RaceId, skillId);
				yield break;
			}

			// Cancel previous skill
			var activeSkill = this.Creature.Skills.ActiveSkill;
			if (activeSkill != null && activeSkill.Info.Id != skillId)
				this.ExecuteOnce(this.CancelSkill());

			stacks = Math2.Clamp(1, skill.RankData.StackMax, skill.Stacks + stacks);
			while (skill.Stacks < stacks)
			{
				// Explicit handling
				if (skillId == SkillId.WebSpinning)
				{
					var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<WebSpinning>(skillId);
					skillHandler.Prepare(this.Creature, skill, null);
					this.Creature.Skills.ActiveSkill = skill;
					skillHandler.Complete(this.Creature, skill, null);
				}
				// Try to handle implicitly
				else
				{
					// Get preparable handler
					var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<IPreparable>(skillId);
					if (skillHandler == null)
					{
						Log.Unimplemented("AI.PrepareSkill: Missing handler or IPreparable for '{0}'.", skillId);
						yield break;
					}

					// Get readyable handler.
					// TODO: There are skills that don't have ready, but go right to
					//   use from Prepare. Handle somehow.
					var readyHandler = skillHandler as IReadyable;
					if (readyHandler == null)
					{
						Log.Unimplemented("AI.PrepareSkill: Missing IReadyable for '{0}'.", skillId);
						yield break;
					}

					this.SharpMind(skillId, SharpMindStatus.Loading);

					// Prepare skill
					try
					{
						if (!skillHandler.Prepare(this.Creature, skill, null))
							yield break;

						this.Creature.Skills.ActiveSkill = skill;
						skill.State = SkillState.Prepared;
					}
					catch (NullReferenceException)
					{
						Log.Warning("AI.PrepareSkill: Null ref exception while preparing '{0}', skill might have parameters.", skillId);
					}
					catch (NotImplementedException)
					{
						Log.Unimplemented("AI.PrepareSkill: Skill prepare method for '{0}'.", skillId);
					}

					// Wait for loading to be done
					foreach (var action in this.Wait(skill.RankData.LoadTime))
						yield return action;

					// Call ready
					readyHandler.Ready(this.Creature, skill, null);
					skill.State = SkillState.Ready;

					this.SharpMind(skillId, SharpMindStatus.Loaded);
				}

				// If stacks are still 0 after preparing, we'll have to assume
				// that the skill didn't set it. We have to break the loop,
				// otherwise the AI would prepare the skill indefinitely.
				if (skill.Stacks == 0)
					break;
			}
		}

		/// <summary>
		/// Makes creature cancel currently loaded skill.
		/// </summary>
		/// <returns></returns>
		protected IEnumerable CancelSkill()
		{
			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			if (this.Creature.Skills.ActiveSkill != null)
			{
				this.SharpMind(this.Creature.Skills.ActiveSkill.Info.Id, SharpMindStatus.Cancelling);
				this.Creature.Skills.CancelActiveSkill();
			}

			yield break;
		}

		/// <summary>
		/// Makes creature use currently loaded skill.
		/// </summary>
		/// <returns></returns>
		protected IEnumerable UseSkill()
		{
			var activeSkillId = this.Creature.Skills.ActiveSkill != null ? this.Creature.Skills.ActiveSkill.Info.Id : SkillId.None;
			if (activeSkillId == SkillId.None)
				yield break;

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			if (activeSkillId == SkillId.Windmill)
			{
				var wmHandler = ChannelServer.Instance.SkillManager.GetHandler<Windmill>(activeSkillId);
				wmHandler.Use(this.Creature, this.Creature.Skills.ActiveSkill, 0, 0, 0);
				this.SharpMind(activeSkillId, SharpMindStatus.Cancelling);
			}
			else if (activeSkillId == SkillId.Stomp)
			{
				var handler = ChannelServer.Instance.SkillManager.GetHandler<Stomp>(activeSkillId);
				handler.Use(this.Creature, this.Creature.Skills.ActiveSkill, 0, 0, 0);
				this.SharpMind(activeSkillId, SharpMindStatus.Cancelling);
			}
			else
			{
				Log.Unimplemented("AI.UseSkill: Skill '{0}'", activeSkillId);
			}
		}

		/// <summary>
		/// Makes creature complete the currently loaded skill.
		/// </summary>
		/// <returns></returns>
		public IEnumerable CompleteSkill()
		{
			if (this.Creature.Skills.ActiveSkill == null)
				yield break;

			var skill = this.Creature.Skills.ActiveSkill;
			var skillId = this.Creature.Skills.ActiveSkill.Info.Id;

			// Get skill handler
			var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<ICompletable>(skillId);
			if (skillHandler == null)
			{
				Log.Unimplemented("AI.CompleteSkill: Missing handler or ICompletable for '{0}'.", skillId);
				yield break;
			}

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			// Run complete
			try
			{
				skillHandler.Complete(this.Creature, skill, null);
			}
			catch (NullReferenceException)
			{
				Log.Warning("AI.CompleteSkill: Null ref exception while preparing '{0}', skill might have parameters.", skillId);
			}
			catch (NotImplementedException)
			{
				Log.Unimplemented("AI.CompleteSkill: Skill complete method for '{0}'.", skillId);
			}

			// Finalize complete or ready again
			if (skill.Stacks == 0)
			{
				this.Creature.Skills.ActiveSkill = null;
				skill.State = SkillState.Completed;
				this.SharpMind(skillId, SharpMindStatus.Cancelling);
			}
			else if (skill.State != SkillState.Canceled)
			{
				skill.State = SkillState.Ready;
			}
		}

		/// <summary>
		/// Makes creature start given skill.
		/// </summary>
		/// <param name="skillId"></param>
		/// <returns></returns>
		protected IEnumerable StartSkill(SkillId skillId)
		{
			// Get skill
			var skill = this.Creature.Skills.Get(skillId);
			if (skill == null)
			{
				Log.Warning("AI.StartSkill: AI '{0}' tried to start skill '{2}', that its creature '{1}' doesn't have.", this.GetType().Name, this.Creature.RaceId, skillId);
				yield break;
			}

			// Get handler
			var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<IStartable>(skillId);
			if (skillHandler == null)
			{
				Log.Unimplemented("AI.StartSkill: Missing handler or interface for '{0}'.", skillId);
				yield break;
			}

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			// Run handler
			try
			{
				if (skillHandler is Rest)
				{
					var restHandler = (Rest)skillHandler;
					restHandler.Start(this.Creature, skill, MabiDictionary.Empty);
				}
				else
				{
					skillHandler.Start(this.Creature, skill, null);
				}
			}
			catch (NullReferenceException)
			{
				Log.Warning("AI.StartSkill: Null ref exception while starting '{0}', skill might have parameters.", skillId);
			}
			catch (NotImplementedException)
			{
				Log.Unimplemented("AI.StartSkill: Skill start method for '{0}'.", skillId);
			}
		}

		/// <summary>
		/// Makes creature stop given skill.
		/// </summary>
		/// <param name="skillId"></param>
		/// <returns></returns>
		protected IEnumerable StopSkill(SkillId skillId)
		{
			// Get skill
			var skill = this.Creature.Skills.Get(skillId);
			if (skill == null)
			{
				Log.Warning("AI.StopSkill: AI '{0}' tried to stop skill '{2}', that its creature '{1}' doesn't have.", this.GetType().Name, this.Creature.RaceId, skillId);
				yield break;
			}

			// Get handler
			var skillHandler = ChannelServer.Instance.SkillManager.GetHandler<IStoppable>(skillId);
			if (skillHandler == null)
			{
				Log.Unimplemented("AI.StopSkill: Missing handler or interface for '{0}'.", skillId);
				yield break;
			}

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			// Run handler
			try
			{
				if (skillHandler is Rest)
				{
					var restHandler = (Rest)skillHandler;
					restHandler.Stop(this.Creature, skill, MabiDictionary.Empty);
				}
				else
				{
					skillHandler.Stop(this.Creature, skill, null);
				}
			}
			catch (NullReferenceException)
			{
				Log.Warning("AI.StopSkill: Null ref exception while stopping '{0}', skill might have parameters.", skillId);
			}
			catch (NotImplementedException)
			{
				Log.Unimplemented("AI.StopSkill: Skill stop method for '{0}'.", skillId);
			}
		}

		/// <summary>
		/// Switches to the given weapon set.
		/// </summary>
		/// <param name="set"></param>
		/// <returns></returns>
		protected IEnumerable SwitchTo(WeaponSet set)
		{
			if (this.Creature.Inventory.WeaponSet == set)
				yield break;

			// Wait until creature isn't stunned anymore
			if (this.Creature.IsStunned)
				yield return true;

			// Wait a moment before and after switching,
			// to let the animation play.
			var waitTime = 500;

			foreach (var action in this.Wait(waitTime))
				yield return action;

			this.Creature.Inventory.ChangeWeaponSet(set);

			foreach (var action in this.Wait(waitTime))
				yield return action;
		}

		/// <summary>
		/// Changes the AI's creature's height.
		/// </summary>
		/// <param name="height"></param>
		/// <returns></returns>
		protected IEnumerable SetHeight(double height)
		{
			this.Creature.Height = (float)height;
			Send.CreatureBodyUpdate(this.Creature);

			yield break;
		}

		/// <summary>
		/// Plays sound effect in rage of AI's creature.
		/// </summary>
		/// <param name="file"></param>
		/// <returns></returns>
		protected IEnumerable PlaySound(string file)
		{
			Send.PlaySound(this.Creature, file);

			yield break;
		}

		/// <summary>
		/// Sets base stat to given value.
		/// </summary>
		/// <param name="stat"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		protected IEnumerable SetStat(Stat stat, float value)
		{
			switch (stat)
			{
				case Stat.Str: this.Creature.StrBase = value; break;
				case Stat.Int: this.Creature.IntBase = value; break;
				case Stat.Dex: this.Creature.DexBase = value; break;
				case Stat.Will: this.Creature.WillBase = value; break;
				case Stat.Luck: this.Creature.LuckBase = value; break;
				default:
					Log.Warning("AI.SetState: Unhandled stat: {0}", stat);
					break;
			}

			yield break;
		}

		/// <summary>
		/// Changes armor in sequence, starting the first item id that
		/// matches the current armor.
		/// </summary>
		/// <example>
		/// itemIds = [15046, 15047, 15048, 15049, 15050]
		/// If current armor is 15046, it's changed to 15047,
		/// if current armor is 15047, it's changed to 15048,
		/// and so on, until there are no more ids.
		/// 
		/// The first id needs to be the default armor, otherwise no
		/// change will occur, since no starting point can be found.
		/// If a creature doesn't have any armor, 0 can be used as the
		/// default, to make it put on armor.
		/// 
		/// Duplicate item ids will not work.
		/// </example>
		/// <param name="itemIds"></param>
		protected IEnumerable SwitchArmor(params int[] itemIds)
		{
			if (itemIds == null || itemIds.Length == 0)
				throw new ArgumentException("A minimum of 1 item id is required.");

			var current = 0;
			var newItemId = -1;

			// Get current item
			var item = this.Creature.Inventory.GetItemAt(Pocket.Armor, 0, 0);
			if (item != null)
				current = item.Info.Id;

			// Search for next item id
			for (int i = 0; i < itemIds.Length - 1; ++i)
			{
				if (itemIds[i] == current)
				{
					newItemId = itemIds[i + 1];
					break;
				}
			}

			// No new id, current not found or end reached
			if (newItemId == -1)
				yield break;

			// Create new item
			Item newItem = null;
			if (newItemId != 0)
			{
				newItem = new Item(newItemId);
				if (item != null)
				{
					// Use same color as the previous armor. Succubi go through
					// more and more revealing clothes, making it look like they
					// lose them, but the colors are variable if we don't set them.
					newItem.Info.Color1 = item.Info.Color1;
					newItem.Info.Color2 = item.Info.Color2;
					newItem.Info.Color3 = item.Info.Color3;
				}
			}

			// Equip new item and remove old one
			if (item != null)
				this.Creature.Inventory.Remove(item);
			if (newItem != null)
				this.Creature.Inventory.Add(newItem, Pocket.Armor);

			yield break;
		}
	}
}

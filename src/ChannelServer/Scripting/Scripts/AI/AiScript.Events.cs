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

namespace Aura.Channel.Scripting.Scripts.AI
{
	public abstract partial class AiScript : IScript, IDisposable
	{
		/// <summary>
		/// Called when creature is hit.
		/// </summary>
		/// <param name="action"></param>
		public virtual void OnTargetActionHit(TargetAction action)
		{
			if (this.Creature.Skills.ActiveSkill != null)
			{
				this.SharpMind(this.Creature.Skills.ActiveSkill.Info.Id, SharpMindStatus.Cancelling);
			}

			lock (_reactions)
			{
				var state = _reactions[_state];
				var ev = AiEvent.None;
				var fallback = AiEvent.None;

				// Knock down event
				if (action.Has(TargetOptions.KnockDown) || action.Has(TargetOptions.Smash))
				{
					// Windmill doesn't trigger the knock down event
					if (action.AttackerSkillId != SkillId.Windmill)
					{
						if (action.Has(TargetOptions.Critical))
							ev = AiEvent.CriticalKnockDown;
						else
							ev = AiEvent.KnockDown;
					}
				}
				// Defense event
				else if (action.SkillId == SkillId.Defense)
				{
					ev = AiEvent.DefenseHit;
				}
				// Magic hit event
				// Use skill ids for now, until we know more about what
				// exactly classifies as a magic hit and what doesn't.
				else if (action.AttackerSkillId >= SkillId.Lightningbolt && action.AttackerSkillId <= SkillId.Inspiration)
				{
					ev = AiEvent.MagicHit;
					if (action.Has(TargetOptions.Critical))
						fallback = AiEvent.CriticalHit;
					else
						fallback = AiEvent.Hit;
				}
				// Hit event
				else
				{
					if (action.Has(TargetOptions.Critical))
						ev = AiEvent.CriticalHit;
					else
						ev = AiEvent.Hit;
				}

				// Try to find and execute event
				Dictionary<SkillId, Func<IEnumerable>> evs = null;
				if (state.ContainsKey(ev))
					evs = state[ev];
				else if (state.ContainsKey(fallback))
					evs = state[fallback];

				if (evs != null)
				{
					// Since events can be defined for specific skills,
					// but assumingly still trigger the default events if no
					// skill specific event was defined, we have to check for
					// the specific skill first, and then fall back to "None",
					// for non skill specific events. If both weren't found,
					// we fall through to clear, since only a skill specific
					// event for a different skill was defined, and we still
					// have to reset the current action.

					// Try skill specific event
					if (evs.ContainsKey(action.AttackerSkillId))
					{
						this.SwitchAction(evs[action.AttackerSkillId]);
						return;
					}
					// Try general event
					else if (evs.ContainsKey(SkillId.None))
					{
						this.SwitchAction(evs[SkillId.None]);
						return;
					}
				}

				// Creature was hit, but there's no event

				// If the queue isn't cleared, the AI won't restart the
				// Aggro state, which will make it keep attacking.
				// This also causes a bug, where when you attack a
				// monster while it's attacking you with Smash,
				// it will keep attacking you with Smash, even though
				// the skill was canceled, due to the received hit.
				// The result is a really confusing situation, where
				// normal looking attacks suddenly break through Defense.
				_curAction = null;
			}
		}

		/// <summary>
		/// Raised from Creature.Kill when creature died,
		/// before active skill is canceled.
		/// </summary>
		/// <param name="creature"></param>
		/// <param name="killer"></param>
		private void OnDeath(Creature creature, Creature killer)
		{
			if (this.Creature.Skills.ActiveSkill != null)
				this.SharpMind(this.Creature.Skills.ActiveSkill.Info.Id, SharpMindStatus.Cancelling);
		}

		/// <summary>
		/// Called when the AI hit someone with a skill.
		/// </summary>
		/// <param name="aAction"></param>
		public void OnUsedSkill(AttackerAction aAction)
		{
			if (this.Creature.Skills.ActiveSkill != null)
				this.ExecuteOnce(this.CompleteSkill());
		}
	}
}

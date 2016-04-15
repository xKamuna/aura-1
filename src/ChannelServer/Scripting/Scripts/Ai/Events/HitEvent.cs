// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Channel.Skills;
using Aura.Mabi.Const;

namespace Aura.Channel.Scripting.Scripts.Ai.Events
{
	/// <summary>
	/// Handles a target combat action.
	/// </summary>
	public class HitEvent : IAiEvent
	{
		/// <summary>
		/// Action to be handled.
		/// </summary>
		public TargetAction TargetAction { get; private set; }

		/// <summary>
		/// Creates new hit event.
		/// </summary>
		/// <param name="targetAction"></param>
		public HitEvent(TargetAction targetAction)
		{
			this.TargetAction = targetAction;
		}

		/// <summary>
		/// Handles hit event.
		/// </summary>
		/// <param name="ai"></param>
		public void Handle(AiScript ai)
		{
			var action = this.TargetAction;
			var activeSkillId = ai.Creature.Skills.ActiveSkillId;

			if (activeSkillId != SkillId.None)
				ai.SharpMind(ai.Creature.Skills.ActiveSkill.Info.Id, SharpMindStatus.Cancelling);

			var ev = AiEventType.None;
			var fallback = AiEventType.None;

			// Knock down event
			if (action.Has(TargetOptions.KnockDown) || action.Has(TargetOptions.Smash))
			{
				// Windmill doesn't trigger the knock down event
				if (action.AttackerSkillId != SkillId.Windmill)
				{
					if (action.Has(TargetOptions.Critical))
						ev = AiEventType.CriticalKnockDown;
					else
						ev = AiEventType.KnockDown;
				}
			}
			// Defense event
			else if (action.SkillId == SkillId.Defense)
			{
				ev = AiEventType.DefenseHit;
			}
			// Magic hit event
			// Use skill ids for now, until we know more about what
			// exactly classifies as a magic hit and what doesn't.
			else if (action.AttackerSkillId >= SkillId.Lightningbolt && action.AttackerSkillId <= SkillId.Inspiration)
			{
				ev = AiEventType.MagicHit;
				if (action.Has(TargetOptions.Critical))
					fallback = AiEventType.CriticalHit;
				else
					fallback = AiEventType.Hit;
			}
			// Hit event
			else
			{
				if (action.Has(TargetOptions.Critical))
					ev = AiEventType.CriticalHit;
				else
					ev = AiEventType.Hit;
			}

			// Try to find and execute event
			var reactions = ai.GetReactions(ai.State, ev);
			if (reactions == null)
				reactions = ai.GetReactions(ai.State, fallback);

			if (reactions != null)
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
				if (reactions.ContainsKey(action.AttackerSkillId))
				{
					ai.SwitchAction(reactions[action.AttackerSkillId]);
					return;
				}
				// Try general event
				else if (reactions.ContainsKey(SkillId.None))
				{
					ai.SwitchAction(reactions[SkillId.None]);
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
			ai.ClearAction();
		}
	}
}

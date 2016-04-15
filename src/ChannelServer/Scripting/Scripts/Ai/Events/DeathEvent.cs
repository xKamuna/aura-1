// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

using Aura.Mabi.Const;

namespace Aura.Channel.Scripting.Scripts.Ai.Events
{
	/// <summary>
	/// Handles the AI creature dying.
	/// </summary>
	public class DeathEvent : IAiEvent
	{
		public void Handle(AiScript ai)
		{
			if (ai.Creature.Skills.ActiveSkill != null)
				ai.SharpMind(ai.Creature.Skills.ActiveSkill.Info.Id, SharpMindStatus.Cancelling);
		}
	}
}

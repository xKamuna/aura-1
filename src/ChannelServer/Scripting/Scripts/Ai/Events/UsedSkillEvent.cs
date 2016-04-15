// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

namespace Aura.Channel.Scripting.Scripts.Ai.Events
{
	/// <summary>
	/// Handles completing a skill after the AI used it.
	/// </summary>
	public class UsedSkillEvent : IAiEvent
	{
		public void Handle(AiScript ai)
		{
			if (ai.Creature.Skills.ActiveSkill != null)
				ai.ExecuteOnce(ai.CompleteSkill());
		}
	}
}

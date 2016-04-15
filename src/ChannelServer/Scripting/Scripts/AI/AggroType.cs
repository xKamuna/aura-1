// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

namespace Aura.Channel.Scripting.Scripts.AI
{
	public enum AggroType
	{
		/// <summary>
		/// Stays in Idle unless provoked
		/// </summary>
		Passive,

		/// <summary>
		/// Goes into alert, but doesn't attack unprovoked.
		/// </summary>
		Careful,

		/// <summary>
		/// Goes into alert and attacks if target is in battle mode.
		/// </summary>
		CarefulAggressive,

		/// <summary>
		/// Goes straight into alert and aggro.
		/// </summary>
		Aggressive,
	}
}

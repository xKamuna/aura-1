// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

namespace Aura.Channel.Scripting.Scripts.AI
{
	public enum AggroLimit
	{
		/// <summary>
		/// Only auto aggroes if no other creature of the same race
		/// aggroed target yet.
		/// </summary>
		One = 1,

		/// <summary>
		/// Only auto aggroes if at most one other creature of the same
		/// race aggroed target.
		/// </summary>
		Two,

		/// <summary>
		/// Only auto aggroes if at most two other creatures of the same
		/// race aggroed target.
		/// </summary>
		Three,

		/// <summary>
		/// Auto aggroes regardless of other enemies.
		/// </summary>
		None = int.MaxValue,
	}
}

//--- Aura Script -----------------------------------------------------------
// Rat Man AI
//--- Description -----------------------------------------------------------
// AI for Rat Man.
//---------------------------------------------------------------------------

[AiScript("ratman")]
public class RatManAi : AiScript
{
	public RatManAi()
	{
		SetVisualField(950, 120);
		SetAggroRadius(400);

		Hates("/pc/", "/pet/");

		On(AiState.Aggro, AiEvent.DefenseHit, OnDefenseHit);
		On(AiState.Aggro, AiEvent.KnockDown, OnKnockDown);
	}

	protected override IEnumerable Idle()
	{
		Do(Wander());
		Do(Wait(2000, 5000));
	}

	protected override IEnumerable Aggro()
	{
		SwitchRandom();
		if (Case(30))
		{
			Do(KeepDistance(1000, false, 2000));
			Do(Circle(600, 1000, 2000));
		}
		else if (Case(20))
		{
			Do(CancelSkill());
			Do(Attack(3));
		}
		else if (Case(20))
		{
			Do(PrepareSkill(SkillId.Smash));
			Do(CancelSkill());
			Do(Attack(3));
		}
		else if (Case(20))
		{
			Do(PrepareSkill(SkillId.Smash));
			Do(Attack(1, 4000));
		}
		else if (Case(5))
		{
			Do(PrepareSkill(SkillId.Defense));
			Do(Follow(600, true));
			Do(CancelSkill());
		}
		else if (Case(5))
		{
			Do(PrepareSkill(SkillId.Counterattack));
			//Do(Follow(600, true));
			Do(CancelSkill());
		}
	}

	private IEnumerable OnDefenseHit()
	{
		Do(Attack(3));
		Do(Wait(3000));
	}

	private IEnumerable OnKnockDown()
	{
		Do(Attack(3));
		Do(Wait(3000));
	}
}

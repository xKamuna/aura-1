//--- Aura Script -----------------------------------------------------------
// Cat Sith Magician AI
//--- Description -----------------------------------------------------------
// AI for the Cat Sith Magician.
//---------------------------------------------------------------------------

[AiScript("catsithmagician")]
public class CatSithMagicianAi : AiScript
{
	public CatSithMagicianAi()
	{
		SetVisualField(950, 120);
		SetAggroRadius(400);
		SetAggroLimit(AggroLimit.None);

		Doubts("/pc/", "/pet/");
		HatesNearby(3000);

		On(AiState.Aggro, AiEvent.Hit, OnHit);
		On(AiState.Aggro, AiEvent.KnockDown, OnKnockDown);
		On(AiState.Aggro, AiEvent.DefenseHit, OnDefenseHit);
	}

	protected override IEnumerable Idle()
	{
		Do(Say("", "", ""));

		SwitchRandom();
		if (Case(40))
		{
			Do(Wander(300, 500, Random(100) < 20));
		}
		else if (Case(20))
		{
			Do(Wait(4000, 6000));
		}

		Do(Wait(2000, 5000));
	}

	protected override IEnumerable Alert()
	{
		Do(Say("", "", ""));
		Do(Circle(400, 2000, 3000));
	}

	protected override IEnumerable Aggro()
	{
		SwitchRandom();
		if (Case(30))
		{
			if (Random() < 50)
			{
				Do(Say("", "", ""));
				Do(Wander(200, 200, false));
			}

			Do(Attack(3, 4000));

			SwitchRandom();
			if (Case(40))
			{
				Do(Attack(3, 4000));
			}
			else if (Case(30))
			{
				Do(PrepareSkill(SkillId.Counterattack));
				Do(Wait(1000));
			}
			else if (Case(10))
			{
				Do(PrepareSkill(SkillId.Counterattack));
				Do(Say("", "", ""));
				Do(Wait(500, 2000));
				Do(Say("", "", ""));
				Do(CancelSkill());
			}
			else if (Case(20))
			{
				if (Random() < 30)
					Do(KeepDistance(1000, false, 3000));

				Do(StackAttack(SkillId.Icebolt, Rnd(1, 1, 2)));
			}

			if (Random() < 50)
				Do(Wait(500, 2000));
		}
		else if (Case(10))
		{
			SwitchRandom();
			if (Case(40))
			{
				Do(Say("", "", ""));
				Do(PrepareSkill(SkillId.Smash));
				Do(Wait(1000, 1500));
				Do(Attack(1, 4000));
			}
			else if (Case(30))
			{
				Do(PrepareSkill(SkillId.Smash));
				Do(CancelSkill());
				Do(Attack(3, 4000));
			}
			else if (Case(30))
			{
				Do(PrepareSkill(SkillId.Defense));
				Do(Wait(2000, 6000));
				Do(CancelSkill());
			}

			Do(Wait(1000, 2000));
		}
		else if (Case(10))
		{
			Do(PrepareSkill(SkillId.Defense));

			if (Random(100) < 60)
				Do(Circle(400, 2000));
			else
				Do(Follow(400, true, 5000));

			Do(CancelSkill());
		}
		else if (Case(10))
		{
			Do(Say("", "", ""));

			SwitchRandom();
			if (Case(60))
			{
				Do(Circle(400, 2000));
			}
			else if (Case(20))
			{
				Do(Follow(400, true, 5000));
			}
			else if (Case(20))
			{
				Do(KeepDistance(1000, false, 5000));
			}
		}
		else if (Case(5))
		{
			Do(PrepareSkill(SkillId.Counterattack));
			Do(Wait(1000, 10000));
			Do(CancelSkill());
		}
		else if (Case(10))
		{
			if (Random(100) < 50)
			{
				Do(PrepareSkill(SkillId.Defense));
				Do(KeepDistance(1000, false, 3000));

				if (Random(100) < 70)
				{
					Do(Wait(1000, 2000));
					Do(CancelSkill());
				}
				else
				{
					Do(CancelSkill());
					Do(Wait(1000, 2000));
				}
			}
			else
			{
				Do(PrepareSkill(SkillId.Counterattack));
				Do(Wait(3000, 7000));
				Do(CancelSkill());
				Do(Wait(1000, 2000));
			}
		}
		else if (Case(25))
		{
			if (Random() < 30)
				Do(KeepDistance(1000, false, 3000));

			Do(StackAttack(SkillId.Icebolt, Rnd(1, 1, 2)));
		}
	}

	private IEnumerable OnHit()
	{
		Do(Say("", "", ""));

		if (Random(100) < 80)
			Do(Attack(3, 4000));
		else
			Do(KeepDistance(10000, false, 2000));
	}

	private IEnumerable OnKnockDown()
	{
		Do(Say("", "", ""));

		SwitchRandom();
		if (Case(40))
		{
			Do(PrepareSkill(SkillId.Windmill));
			Do(Wait(3000, 4000));
			Do(Say("", "", ""));
			Do(UseSkill());
		}
		else if (Case(30))
		{
			Do(PrepareSkill(SkillId.Defense));

			if (Random(100) < 60)
				Do(Circle(400, 2000));
			else
				Do(Follow(400, true, 5000));

			Do(CancelSkill());
		}
		else if (Case(5))
		{
			Do(PrepareSkill(SkillId.Smash));
			Do(Say("", "", ""));
			Do(Attack(1, 4000));
		}
	}

	private IEnumerable OnDefenseHit()
	{
		if (Random(100) < 40)
		{
			Do(Attack(3, 4000));

			if (Random(100) < 50)
				Do(Wait(1000, 2000));
		}
		else
		{
			Do(Attack(1, 4000));
			Do(Wait(2000));
			Do(Attack(1, 4000));
			Do(Wait(2000));
			Do(Attack(1, 4000));
			Do(Wait(2000));
			Do(Attack(1, 4000));
		}
	}
}

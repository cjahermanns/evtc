using System.Collections.Generic;
using System.Linq;
using RotationComparison.Rotations;
using ScratchEVTCParser;
using ScratchEVTCParser.Model;
using ScratchEVTCParser.Model.Agents;
using ScratchEVTCParser.Statistics;
using ScratchEVTCParser.Statistics.RotationItems;
using RotationItem = RotationComparison.Rotations.RotationItem;
using SkillCastType = RotationComparison.Rotations.SkillCastType;

namespace RotationComparison.Logs
{
	public abstract class ScratchParserLogSource : ILogSource
	{
		private string[] characterNames;

		public void SetCharacterNameFilter(string[] names)
		{
			characterNames = names;
		}

		protected abstract Log GetLog();

		public IEnumerable<Rotation> GetRotations()
		{
			var log = GetLog();
			var players = log.Agents.OfType<Player>();
			if (characterNames != null)
			{
				players = players.Where(x => characterNames.Contains(x.Name));
			}

			var rotationCalculator = new RotationCalculator();
			foreach (var player in players)
			{
				var playerRotation = rotationCalculator.GetRotation(log, player);
				var items = new List<RotationItem>();
				foreach (var item in playerRotation.Items)
				{
					if (item is SkillCastItem skillCast)
					{
						items.Add(GetSkillCast(skillCast));
					}
					else if (item is WeaponSwapItem weaponSwap)
					{
						items.Add(GetWeaponSwap(weaponSwap));
					}
				}
				yield return new Rotation(player.Name, player.Profession, player.EliteSpecialization, items);
			}
		}

		private SkillCast GetSkillCast(SkillCastItem skillCastItem)
		{
			var time = skillCastItem.ItemTime;
			var skillId = skillCastItem.Skill.Id;
			var skillName = skillCastItem.Skill.Name;
			var duration = skillCastItem.Duration;
			SkillCastType type;
			switch (skillCastItem.Type)
			{
				case ScratchEVTCParser.Statistics.RotationItems.SkillCastType.Success:
					type = SkillCastType.Success;
					break;
				case ScratchEVTCParser.Statistics.RotationItems.SkillCastType.Cancel:
					type = SkillCastType.Cancel;
					break;
				case ScratchEVTCParser.Statistics.RotationItems.SkillCastType.Reset:
					type = SkillCastType.Reset;
					break;
				default:
					type = SkillCastType.Unknown;
					break;
			}

			return new SkillCast(time, duration, type, skillId, skillName);
		}

		private WeaponSwap GetWeaponSwap(WeaponSwapItem weaponSwapItem)
		{
            return new WeaponSwap(weaponSwapItem.ItemTime, weaponSwapItem.NewWeaponSet);
		}

		public string GetEncounterName()
		{
			var log = GetLog();
			var calculator = new StatisticsCalculator();
			return calculator.GetEncounter(log).GetName();
		}


		public abstract string GetLogName();
	}
}
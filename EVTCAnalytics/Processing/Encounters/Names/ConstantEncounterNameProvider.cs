using GW2Scratch.EVTCAnalytics.GameData;

namespace GW2Scratch.EVTCAnalytics.Processing.Encounters.Names
{
	/// <summary>
	/// A provider that always provides the same value.
	/// </summary>
	public class ConstantEncounterNameProvider : IEncounterNameProvider
	{
		private readonly string encounterName;

		public ConstantEncounterNameProvider(string encounterName)
		{
			this.encounterName = encounterName;
		}

		public string GetEncounterName(IEncounterData encounterData, GameLanguage logLanguage)
		{
			return encounterName;
		}
	}
}
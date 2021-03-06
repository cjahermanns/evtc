using System.Collections.Generic;
using System.Linq;
using GW2Scratch.EVTCAnalytics.GameData.Encounters;
using GW2Scratch.EVTCAnalytics.Model.Agents;
using GW2Scratch.EVTCAnalytics.Processing.Encounters.Modes;
using GW2Scratch.EVTCAnalytics.Processing.Encounters.Results;
using GW2Scratch.EVTCAnalytics.Processing.Encounters.Results.Health;
using GW2Scratch.EVTCAnalytics.Processing.Steps;

namespace GW2Scratch.EVTCAnalytics.Processing.Encounters
{
	/// <summary>
	/// A simple encounter data container with all properties configurable on creation.
	/// </summary>
	public class BaseEncounterData : IEncounterData
	{
		public Encounter Encounter { get; }
		public IResultDeterminer ResultDeterminer { get; }
		public IModeDeterminer ModeDeterminer { get; }
		public IHealthDeterminer HealthDeterminer { get; }
		public IReadOnlyList<IPostProcessingStep> ProcessingSteps { get; }
		public List<Agent> Targets { get; }

		public BaseEncounterData(
			Encounter encounter,
			IEnumerable<Agent> importantAgents,
			IResultDeterminer resultDeterminer,
			IModeDeterminer modeDeterminer,
			IHealthDeterminer healthDeterminer,
			IReadOnlyList<IPostProcessingStep> processingSteps)
		{
			Targets = importantAgents.ToList();
			Encounter = encounter;
			ResultDeterminer = resultDeterminer;
			ModeDeterminer = modeDeterminer;
			HealthDeterminer = healthDeterminer;
			ProcessingSteps = processingSteps;
		}
	}
}
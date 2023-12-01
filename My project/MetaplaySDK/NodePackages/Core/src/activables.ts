// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import type { Variant } from '@metaplay/meta-ui-next'

interface PhaseInfo {
  sortOrder: number
  displayString: string
  badgeVariant: Variant
  tooltip: string
}

const playerPhaseInfos: { [index: string]: PhaseInfo } = {
  Preview: {
    sortOrder: 1,
    displayString: 'Preview',
    badgeVariant: 'primary',
    tooltip: 'The [TYPENAME] is in it\'s preview phase.'
  },
  Tentative: {
    sortOrder: 2,
    displayString: 'Available',
    badgeVariant: 'primary',
    tooltip: 'The [TYPENAME] is available for [PLAYER] to activate.'
  },
  Active: {
    sortOrder: 3,
    displayString: 'Active',
    badgeVariant: 'success',
    tooltip: 'The [TYPENAME] is active and visible to [PLAYER].'
  },
  EndingSoon: {
    sortOrder: 4,
    displayString: 'Ending Soon',
    badgeVariant: 'success',
    tooltip: 'The [TYPENAME] is active but will be ending soon.'
  },
  Review: {
    sortOrder: 5,
    displayString: 'Review',
    badgeVariant: 'primary',
    tooltip: 'Not active any more, but still visible to [PLAYER].'
  },
  Inactive: {
    sortOrder: 6,
    displayString: 'Not Available',
    badgeVariant: 'neutral',
    tooltip: 'Not available or visible to [PLAYER].'
  },
  InCooldown: {
    sortOrder: 7,
    displayString: 'On Cooldown',
    badgeVariant: 'neutral',
    tooltip: 'On cooldown after previous active phase.'
  },
  TotalLimitsReached: {
    sortOrder: 8,
    displayString: 'Limits Reached',
    badgeVariant: 'neutral',
    tooltip: 'Activation or consumption limits have been reached.'
  },
  Ineligible: {
    sortOrder: 9,
    displayString: 'Ineligible',
    badgeVariant: 'neutral',
    tooltip: '[PLAYER] does not fulfill the requirements.'
  },
  NoSchedule: {
    sortOrder: 10,
    displayString: 'Dynamic',
    badgeVariant: 'primary',
    tooltip: 'Available depending on game state.'
  },
  ServerError: {
    sortOrder: 11,
    displayString: 'Server Error',
    badgeVariant: 'warning',
    tooltip: 'The server failed to resolve the phase.'
  },
  Disabled: {
    sortOrder: 12,
    displayString: 'Disabled',
    badgeVariant: 'danger',
    tooltip: 'The [TYPENAME] is disabled.'
  }
}

const nonPlayerPhaseInfos: { [index: string]: PhaseInfo } = {
  Preview: {
    sortOrder: 1,
    displayString: 'In Preview',
    badgeVariant: 'primary',
    tooltip: 'Visible to players but not available yet.'
  },
  Active: {
    sortOrder: 2,
    displayString: 'Available',
    badgeVariant: 'success',
    tooltip: 'Available and visible to players.'
  },
  EndingSoon: {
    sortOrder: 3,
    displayString: 'Ending Soon',
    badgeVariant: 'success',
    tooltip: 'Available and visible to players, but ending soon.'
  },
  Review: {
    sortOrder: 4,
    displayString: 'In Review',
    badgeVariant: 'primary',
    tooltip: 'Not available any more, but still visible to players.'
  },
  Inactive: {
    sortOrder: 5,
    displayString: 'Not Available',
    badgeVariant: 'neutral',
    tooltip: 'Not available or visible to players.'
  },
  NoSchedule: {
    sortOrder: 6,
    displayString: 'Dynamic',
    badgeVariant: 'primary',
    tooltip: 'Available depending on the players\' game state.'
  },
  ServerError: {
    sortOrder: 7,
    displayString: 'Server Error',
    badgeVariant: 'warning',
    tooltip: 'The server failed to resolve the phase.'
  },
  Disabled: {
    sortOrder: 8,
    displayString: 'Disabled',
    badgeVariant: 'danger',
    tooltip: 'The [TYPENAME] is disabled.'
  }
}

// \todo The following types closely follow the corresponding C# types,
//       but so far these only specify the properties which are actually
//       needed in these utilities. More properties could be added here.

// Corresponds to C# MetaActivableParams
interface ActivableParams {
  isEnabled: boolean
  // \todo There's many other params, add them as needed
}

// Corresponds to C# IMetaActivableConfigData
interface ActivableConfig {
  activableParams: ActivableParams
  // \todo displayName, description, displayShortInfo
}

// Corresponds to C# SchedulePhaseInfo in ActivablesControllerBase.cs
interface ActivableSchedulePhaseInfo {
  phase: string
  // \todo startTime, endTime
}

// Corresponds to C# ScheduleStatus in ActivablesControllerBase.cs
interface ActivableScheduleStatus {
  currentPhase: ActivableSchedulePhaseInfo
  // \todo nextPhase
}

// Corresponds to C# GeneralActivableData
interface GeneralActivableData {
  config: ActivableConfig
  scheduleStatus: ActivableScheduleStatus | null
  // \todo evaluatedAt, audienceSizeEstimate, statistics
}

// Corresponds to C# PlayerActivableData
interface PlayerActivableData {
  config: ActivableConfig
  phase: string
  scheduleStatus: ActivableScheduleStatus | null
  // \todo state, debugState
}

/**
 * Data about an activable as returned by the "activables/..." or "offers/..." Admin APIs.
 * This may be either player-specific activable data (PlayerActivableData) or general data about an activable (GeneralActivableData).
 */
export type ActivableData = GeneralActivableData | PlayerActivableData

/**
 * Get info about an activable's phase.
 * @param {ActivableData} activable Data about the activable. If it's PlayerActivableData, `player` should be non-null.
 * @param overridePhase null in order to deduce phase based on `activable`, or non-null to override that with a specific phase.
 * @param player The player's state if this concerns an activable's state on a specific player; null if this concerns an activable in general.
 * @returns Info about the activable's phase.
 */
// TODO: this could benefit from proper typings. Maybe an r'n'd opportunity for generated C# typings?
function getPhaseInfo (activable: ActivableData, overridePhase: string | null, player: object | null): PhaseInfo {
  const source = player ? playerPhaseInfos : nonPlayerPhaseInfos

  let phase
  if (overridePhase === null) {
    if (activable.config.activableParams.isEnabled) {
      if (player) {
        if ('phase' in activable) {
          phase = activable.phase
        } else {
          // \todo activable.phase shouldn't be missing if `player` is non-null, i.e.
          //       activable should be PlayerActivableData if `player` is non-null.
          //       How to express that with types?
          throw new Error('Error getting activable phaseInfo. Is the player data wrapped in a ref?')
        }
      } else {
        phase = activable.scheduleStatus?.currentPhase.phase ?? 'NoSchedule'
      }
    } else {
      phase = 'Disabled'
    }
  } else {
    phase = overridePhase
  }

  return source[phase]
}

/**
 * Get the display strings of the various activable phases.
 * @param hasPlayer Whether to get the phases regarding player-specific activable states, as opposed to activable scheduling in general.
 * @param hideErrorStrings Whether to exclude phases describing an internal error.
 * @returns The display strings of the activable phases.
 */
export function allPhaseDisplayStrings (hasPlayer: boolean, hideErrorStrings: boolean = true): string[] {
  const source = hasPlayer ? playerPhaseInfos : nonPlayerPhaseInfos
  let displayStrings = Object.values(source).map(x => x.displayString)
  if (hideErrorStrings) {
    displayStrings = displayStrings.filter(x => x !== 'Server Error')
  }
  return displayStrings
}

/**
 * Get a number to compare activables when sorting them by their phases.
 * @see getPhaseInfo about the parameters.
 */
export function phaseSortOrder (activable: ActivableData, overridePhase: string | null = null, player: any = null): number {
  return getPhaseInfo(activable, overridePhase, player)?.sortOrder || -1
}

/**
 * Get a phase's display string.
 * @see getPhaseInfo about the parameters.
 */
export function phaseDisplayString (activable: ActivableData, overridePhase: string | null = null, player: any = null): string {
  return getPhaseInfo(activable, overridePhase, player)?.displayString || `Error: ${overridePhase}`
}

/**
 * Get a description of the phase, for tooltips.
 * @see getPhaseInfo about the parameters.
 * @param typeName The name of the type of the activable, used in some of the description texts. For example, "event".
 */
export function phaseTooltip (activable: ActivableData, overridePhase: string | null = null, player: any = null, typeName: string | null = null): string {
  return (getPhaseInfo(activable, overridePhase, player)?.tooltip || `Error: ${overridePhase}`)
    .replace(/\[PLAYER\]/g, player?.model?.playerName || 'n/a')
    .replace(/\[TYPENAME\]/g, typeName ?? 'activable')
}

/**
 * Get the color variant of the phase, used in badges.
 * @see getPhaseInfo about the parameters.
 */
export function phaseBadgeVariant (activable: ActivableData, overridePhase: string | null = null, player: any = null): Variant {
  return getPhaseInfo(activable, overridePhase, player)?.badgeVariant || 'warning'
}

/**
 * Get the display string of the given phase.
 */
export function phaseToDisplayString (phase: string): string {
  return playerPhaseInfos[phase].displayString
}

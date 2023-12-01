// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import type { Variant } from '@metaplay/meta-ui-next'
import moment from 'moment'

export function getPhaseVariant (phase: string): Variant {
  if (phase === 'Preview') return 'primary'
  if (phase === 'Active') return 'success'
  if (phase === 'EndingSoon') return 'warning'
  else return 'neutral'
}

// Unused functions in develop most placed, Pennina please remove when you don't need them in your branches anymore.
export function calculateSeasonPhase (seasonData: any) {
  const startTime = moment(seasonData.startTime)
  const endingSoonTime = moment(seasonData.endingSoonStartsAt || seasonData.endTime)
  const endTime = moment(seasonData.endTime)
  const nowTime = moment()

  if (nowTime < startTime) {
    return 'Preview'
  } else if (nowTime < endingSoonTime && nowTime < endTime) {
    return 'Active'
  } else if (nowTime < endTime) {
    return 'EndingSoon'
  } else {
    return 'Inactive'
  }
}

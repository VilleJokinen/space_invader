// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { type IGeneratedUiComponentRule } from '../components/generatedui/generatedUiTypes'
import { useCoreStore } from '../coreStore'

/**
 * Add a new component to the library of generated UI components to be used to render dynamic data.
 * Usually used to teach the dashboard how to render game-specific C# data types.
 * @param rule The rule to add.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addGeneratedUiViewComponent({
      filterFunction: (_props, type) => {
        // If we are rendering a view of a producer...
        return type.typeName === 'Game.Logic.ProducerTypeId'
      },
      // ...then use our custom producerId field component.
      vueComponent: () => import('./ProducerIdViewField.vue')
    })
  })
 */
export function addGeneratedUiViewComponent (rule: IGeneratedUiComponentRule) {
  const coreStore = useCoreStore()
  coreStore.addGeneratedUiViewComponent(rule)
}

/**
 * Add a new component to the library of generated UI components to be used to render dynamic data.
 * Usually used to teach the dashboard how to render game-specific C# data types.
 * @param rule The rule to add.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addGeneratedUiFormComponent({
      filterFunction: (_props, type) => {
        // If we are rendering a form for a producer id...
        return type.typeName === 'Game.Logic.ProducerTypeId'
      },
      // ...then use our custom producerId field component.
      vueComponent: () => import('./ProducerIdPickerField.vue')
    })
  })
 */
export function addGeneratedUiFormComponent (rule: IGeneratedUiComponentRule) {
  const coreStore = useCoreStore()
  coreStore.addGeneratedUiFormComponent(rule)
}

/**
 * When stringIds are rendered in generated forms, the bare stringId alone is often not enough information to be human
 * readable. Add a decorator function to transform the bare stringId into something more readable.
 * @param stringId The type of the stringId to decorate.
 * @param decorator A function to return a descriptive string fort the stringId.
 * @example
    initializationApi.addStringIdDecorator('Game.Logic.ProducerTypeId', (stringId:string): string => {
      const producer = gameData.gameConfig.Producers[stringId]
      const producerKind = gameData.gameConfig.ProducerKinds[producer.kind]
      return `${producer.name} (${producerKind.kind})`
    })
 */
export function addStringIdDecorator (stringId: string, decorator: (stringId: string) => string) {
  const coreStore = useCoreStore()
  coreStore.stringIdDecorators[stringId] = decorator
}

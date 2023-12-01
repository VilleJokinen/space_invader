declare module 'vue' {
  // eslint-disable-next-line
  import { CompatVue } from '@vue/runtime-dom'
  const Vue: CompatVue
  export default Vue
  // eslint-disable-next-line
  export * from '@vue/runtime-dom'
  const { configureCompat } = Vue
  export { configureCompat }
}

/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
    './node_modules/@metaplay/meta-ui-next/src/**/*.{vue,js,ts,jsx,tsx}',
    './node_modules/@metaplay/meta-ui/src/**/*.{vue,js,ts,jsx,tsx}',
    './node_modules/@metaplay/core/src/**/*.{vue,js,ts,jsx,tsx}',
  ],
  plugins: [
    require('@tailwindcss/forms'),
    require('@tailwindcss/container-queries'),
    require('@metaplay/tailwind-plugin')
  ],
}

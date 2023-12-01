import plugin from 'tailwindcss/plugin'
import colors from 'tailwindcss/colors'

const metaUiTailwindPlugin = plugin(function ({ addBase, theme }) {
  // Base CSS styles.
  addBase({
    // Default font color.
    body: {
      color: theme('colors.neutral.900'),
    },
  })
}, {
  prefix: 'tw-', // Prefix for generated CSS classes so we avoid collisions with Bootstrap.
  theme: {
    extend: {
      fontSize: {
        //- The custom font size 0.8125rem below is right in between tailwind xs(0.75rem) and sm(0.875rem).
        'xs+': '0.8125rem',
        //- The custom font size 0.9375rem below is right in between tailwind sm(0.875rem) and base(1rem).
        'sm+': '0.9375rem'
      }
    },
    // Set what colors are available, overriding the defaults.
    colors: {
      transparent: 'transparent',
      current: 'currentColor',
      black: colors.black,
      white: colors.white,
      neutral: colors.neutral,
      red: colors.red,
      orange: {
        50: "#fff2e6",
        100: "#ffe4cc",
        200: "#ffca99",
        300: "#ffaf66",
        400: "#ff9533",
        500: "#ff7a00", // This is Metaplay orange.
        600: "#cc6200",
        700: "#994900",
        800: "#663100",
        900: "#331800"
      },
      green: {
        50: "#ecf0ea",
        100: "#d9e1d6",
        200: "#b2c2ac",
        300: "#8ca483",
        400: "#658559",
        500: "#3f6730", // This is Metaplay green.
        600: "#325226",
        700: "#263e1d",
        800: "#192913",
        900: "#0d150a"
      },
      blue: {
        50: "#eaf4fc",
        100: "#d5e9f8",
        200: "#abd3f1",
        300: "#81bcea",
        400: "#57a6e3",
        500: "#2d90dc", // This is Bootstrap-vue blue.
        600: "#2473b0",
        700: "#1b5684",
        800: "#123a58",
        900: "#091d2c"
      },
    },
  },
})

module.exports = metaUiTailwindPlugin

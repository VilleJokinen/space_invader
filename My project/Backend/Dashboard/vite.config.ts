import { defineConfig } from 'vite'
import vue, { type Options as VuePluginOptions } from '@vitejs/plugin-vue'
import eslintPlugin, { type Options as EslintPluginOptions } from 'vite-plugin-eslint'
import { viteStaticCopy } from 'vite-plugin-static-copy'
import path from 'path'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    vue({
      template: {
        compilerOptions: {
          isCustomElement: tag => tag.startsWith('sl-'), // Declare all Shoelace components as custom elements.
          compatConfig: {
            MODE: 2
          }
        }
      }
    } satisfies VuePluginOptions),
    eslintPlugin({
      exclude: [
        '**/node_modules/**',
        '**/NodePackages/**', // Added our own monorepo packages to ESLint ignore list so they don't get linted twice.
      ]
    } satisfies EslintPluginOptions),
    // Copy Shoelace assets to dist/shoelace
    viteStaticCopy({
      targets: [
        {
          src: path.resolve(__dirname, 'node_modules/@shoelace-style/shoelace/dist/assets'),
          dest: path.resolve(__dirname, 'dist/shoelace')
        }
      ]
    })
  ],
  server: {
    port: 5551,
    strictPort: true,
    watch: {
      ignored: [/\.#/] // Ignore EMACS files
    },
    proxy: {
      '/api': {
        target: 'http://localhost:5550'
      }
    },
  },
  preview: {
    port: 5551
  },
  resolve: {
    alias: {
      vue: '@vue/compat' // Vue 3 compatibility mode setup
    }
  },
  build: {
    emptyOutDir: true,
    cssCodeSplit: false, // Forces all CSS to be bundled into a single file -> better for our project since we have very very small snippets all over the place
    // TODO: consider if we could optimize some of the generated chuncks
    chunkSizeWarningLimit: 2048
  }
})

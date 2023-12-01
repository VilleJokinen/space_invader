const path = require('path');
const webpack = require('webpack');

module.exports = {
  entry: './src/main.ts',
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: 'ts-loader',
        exclude: /node_modules/,
      },
      {
        test: /\.m?js/,
        resolve: { fullySpecified: false },
      },
    ],
  },
  resolve: {
    extensions: ['.tsx', '.ts', '.js'],
    fallback: {
      "url": require.resolve("url/"),
      "os": require.resolve("os-browserify/browser"),
      "https": require.resolve("https-browserify"),
      "http": require.resolve("stream-http"),
      "assert": require.resolve("assert/"),
      "buffer": require.resolve("buffer/"),
      "crypto": require.resolve("crypto-browserify"),
      "stream": require.resolve("stream-browserify"),
    }
  },
  plugins: [
    new webpack.ProvidePlugin({
      Buffer: ['buffer', 'Buffer'],
    }),
    new webpack.ProvidePlugin({
      process: 'process/browser',
    }),
  ],
  output: {
    filename: 'metaplay-imx-link-sdk-plugin.min.js',
    path: path.resolve(__dirname, 'dist'),
    library: 'MetaplayImxLinkSdkPlugin',
  },
};
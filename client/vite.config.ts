import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5098',
    },
  },
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: true,
  },
})

import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const REPO_BASE = '/internal-be-kafka-cache-lib/';

export default defineConfig(({ command }) => ({
	plugins: [react(), tailwindcss()],
	base: command === 'build' ? REPO_BASE : '/',
}));

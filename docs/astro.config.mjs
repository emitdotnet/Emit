import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://emitdotnet.github.io',
  base: process.env.CI ? '/Emit' : undefined,
  vite: {
    server: {
      watch: {
        usePolling: true,
        interval: 500,
      },
    },
  },
  integrations: [
    starlight({
      title: 'Emit',
      description: 'A .NET transactional outbox library with retry logic and ordering guarantees.',
      logo: { src: './src/assets/logo.png' },
      favicon: '/favicon.ico',
      customCss: ['./src/styles/custom.css'],
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/emitdotnet/Emit' },
      ],
      sidebar: [
        { label: 'Introduction', link: '/' },
        {
          label: 'Getting Started',
          items: [
            { label: 'Installation', link: '/getting-started/installation/' },
            { label: 'Quickstart', link: '/getting-started/quickstart/' },
          ],
        },
        {
          label: 'Concepts',
          items: [
            { label: 'Pipeline Model', link: '/concepts/pipeline-model/' },
            { label: 'Leader Election & Daemons', link: '/concepts/leader-election/' },
          ],
        },
        {
          label: 'Mediator',
          items: [
            { label: 'Overview', link: '/mediator/' },
          ],
        },
        {
          label: 'Transactional Outbox',
          items: [
            { label: 'Overview', link: '/outbox/' },
          ],
        },
        {
          label: 'Persistence',
          items: [
            { label: 'MongoDB', link: '/persistence/mongodb/' },
            { label: 'EF Core (PostgreSQL)', link: '/persistence/efcore/' },
            { label: 'Distributed Locks', link: '/persistence/distributed-locks/' },
          ],
        },
        {
          label: 'Kafka',
          items: [
            { label: 'Setup', link: '/kafka/setup/' },
            { label: 'Producers', link: '/kafka/producers/' },
            { label: 'Consumers', link: '/kafka/consumers/' },
            { label: 'Content-Based Routing', link: '/kafka/routing/' },
            { label: 'Serialization', link: '/kafka/serialization/' },
          ],
        },
        {
          label: 'Error Handling',
          items: [
            { label: 'Error Policies', link: '/error-handling/error-policies/' },
            { label: 'Resilience', link: '/error-handling/resilience/' },
            { label: 'Dead Letter Queue', link: '/error-handling/dead-letter-queue/' },
          ],
        },
        {
          label: 'Observability',
          items: [
            { label: 'Distributed Tracing', link: '/observability/tracing/' },
            { label: 'Metrics', link: '/observability/metrics/' },
            { label: 'Observers', link: '/observability/observers/' },
            { label: 'Logging', link: '/observability/logging/' },
            { label: 'Health Checks', link: '/observability/health-checks/' },
          ],
        },
        {
          label: 'Going Deeper',
          collapsed: true,
          items: [
            { label: 'Custom Middleware', link: '/advanced/custom-middleware/' },
            { label: 'Testing', link: '/advanced/testing/' },
            { label: 'Validation', link: '/advanced/validation/' },
            { label: 'Feature Collection', link: '/advanced/feature-collection/' },
            { label: 'Configuration Reference', link: '/advanced/configuration/' },
          ],
        },
      ],
    }),
  ],
});

import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'DigitalBrain',
  description: 'Neurons, synapses, and simulations — durable agents for .NET on Orleans and Aspire',
  cleanUrls: true,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }],
    ['meta', { name: 'theme-color', content: '#080a12' }]
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Quickstart', link: '/quickstart' },
      { text: 'Concepts', link: '/concepts' },
      { text: 'Architecture', link: '/architecture' },
      { text: 'Specification', link: '/specification' },
      { text: 'Packages', link: '/packages/' },
      { text: 'Status', link: '/status' }
    ],
    sidebar: [
      {
        text: 'Start here',
        items: [
          { text: 'Quickstart', link: '/quickstart' },
          { text: 'Concepts', link: '/concepts' },
          { text: 'Architecture', link: '/architecture' },
          { text: 'Specification', link: '/specification' }
        ]
      },
      {
        text: 'Packages',
        items: [
          { text: 'Overview', link: '/packages/' },
          { text: 'DigitalBrain', link: '/packages/metapackage' },
          { text: 'DigitalBrain.Abstractions', link: '/packages/abstractions' },
          { text: 'DigitalBrain.Kernel', link: '/packages/kernel' },
          { text: 'DigitalBrain.Client', link: '/packages/client' },
          { text: 'DigitalBrain.Testing', link: '/packages/testing' },
          { text: 'DigitalBrain.Aspire', link: '/packages/aspire' },
          { text: 'DigitalBrain.Aspire.Hosting', link: '/packages/aspire-hosting' },
          { text: 'DigitalBrain.DevTools', link: '/packages/devtools' },
          { text: 'AI Contracts', link: '/packages/ai-contracts' },
          { text: 'AI Runtime', link: '/packages/ai' },
          { text: 'AI Aspire Hosting', link: '/packages/ai-aspire-hosting' }
        ]
      },
      {
        text: 'Project',
        items: [
          { text: 'Contributing', link: '/contributing' },
          { text: 'Status', link: '/status' }
        ]
      }
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/digitalbraintech/brain' }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'The v2 foundation is being rebuilt in the open. Nothing is published yet.',
      copyright: 'DigitalBrain contributors'
    },
    outline: {
      level: [2, 3],
      label: 'On this page'
    }
  }
})

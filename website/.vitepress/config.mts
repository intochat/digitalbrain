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
      { text: 'Concepts', link: '/concepts' },
      { text: 'Status', link: '/status' }
    ],
    sidebar: [
      {
        text: 'DigitalBrain',
        items: [
          { text: 'Concepts', link: '/concepts' },
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

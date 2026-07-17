import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'DigitalBrain',
  description: 'A small kernel for durable, addressable capabilities',
  cleanUrls: true,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }],
    ['meta', { name: 'theme-color', content: '#080a12' }]
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Getting Started', link: '/getting-started/' },
      { text: 'Concepts', link: '/guide/' },
      { text: 'Build', link: '/build/first-module' },
      { text: 'Status', link: '/reference/status' },
      { text: 'Contributing', link: '/contributing/' }
    ],
    sidebar: {
      '/getting-started/': [
        {
          text: 'Getting Started',
          items: [
            { text: 'Run DigitalBrain', link: '/getting-started/' },
            { text: 'First MCP call', link: '/getting-started/first-call' }
          ]
        }
      ],
      '/guide/': [
        {
          text: 'Concepts',
          items: [
            { text: 'What is DigitalBrain?', link: '/guide/' },
            { text: 'Architecture', link: '/guide/architecture' }
          ]
        },
        {
          text: 'Core model',
          items: [
            { text: 'Neurons', link: '/guide/neurons' },
            { text: 'Synapses', link: '/guide/synapses' },
            { text: 'Modules', link: '/guide/modules' },
            { text: 'Programming model', link: '/guide/programming-model' },
            { text: 'Webhook neurons', link: '/guide/webhooks' }
          ]
        }
      ],
      '/build/': [
        {
          text: 'Build',
          items: [
            { text: 'First module', link: '/build/first-module' }
          ]
        }
      ],
      '/contributing/': [
        {
          text: 'Contributing',
          items: [
            { text: 'Start here', link: '/contributing/' }
          ]
        }
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Implementation status', link: '/reference/status' },
            { text: 'Architecture decisions', link: '/reference/decisions' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/InteractiveAgents/DigitalBrain' }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'Current behavior is documented separately from target architecture.',
      copyright: 'DigitalBrain contributors'
    },
    outline: {
      level: [2, 3],
      label: 'On this page'
    }
  }
})

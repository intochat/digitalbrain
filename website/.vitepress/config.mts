import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'DigitalBrain',
  description: 'An operating system built from neurons and synapses',
  cleanUrls: true,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }],
    ['meta', { name: 'theme-color', content: '#080a12' }]
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Guide', link: '/guide/' },
      { text: 'Architecture', link: '/guide/architecture' },
      { text: 'Contributing', link: '/contributing/' },
      { text: 'Reference', link: '/reference/status' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
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
      message: 'Built in the open from neurons and synapses.',
      copyright: 'DigitalBrain contributors'
    },
    outline: {
      level: [2, 3],
      label: 'On this page'
    }
  }
})

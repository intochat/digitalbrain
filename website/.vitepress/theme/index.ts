import DefaultTheme from 'vitepress/theme'
import HomePage from './components/HomePage.vue'
import NeuronGraph from './components/NeuronGraph.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('HomePage', HomePage)
    app.component('NeuronGraph', NeuronGraph)
  }
}

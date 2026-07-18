import DefaultTheme from 'vitepress/theme'
import BehaviorTabs from './BehaviorTabs.vue'
import ArchitectureDiagram from './components/ArchitectureDiagram.vue'
import HowItWorksDiagram from './components/HowItWorksDiagram.vue'
import HomePage from './components/HomePage.vue'
import BrainView from './components/BrainView.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('BehaviorTabs', BehaviorTabs)
    app.component('ArchitectureDiagram', ArchitectureDiagram)
    app.component('HowItWorksDiagram', HowItWorksDiagram)
    app.component('HomePage', HomePage)
    app.component('BrainView', BrainView)
  }
}

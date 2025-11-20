import './App.css'
import ApiTester from './components/ApiTester'

function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>🚀 SSAFY Digital Twin - Admin Panel</h1>
        <p>BackEnd API 테스트 및 관리 도구</p>
      </header>
      <main className="app-main">
        <ApiTester />
      </main>
    </div>
  )
}

export default App

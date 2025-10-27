import { useState } from 'react'
import axios from 'axios'
import './ApiTester.css'

// 백엔드 URL 동적 구성
const getApiBaseUrl = () => {
  const hostname = window.location.hostname
  // localhost 또는 127.0.0.1인 경우 그대로 사용, 그렇지 않으면 현재 호스트 사용
  if (hostname === 'localhost' || hostname === '127.0.0.1') {
    return 'http://localhost:8000'
  }
  return `http://${hostname}:8000`
}

const API_BASE_URL = getApiBaseUrl()

function ApiTester() {
  const [activeTab, setActiveTab] = useState('health')
  const [responses, setResponses] = useState({})
  const [loading, setLoading] = useState({})
  const [currentApiUrl, setCurrentApiUrl] = useState(API_BASE_URL)
  const [formData, setFormData] = useState({
    group: { meta_data: '{}' },
    scan: { group_id: 1, meta_data: '{}', status: 'UPLOADED', memos: '[]' },
    file: null,
    groupId: 1,
    scanId: 1
  })

  const safeJsonParse = (jsonString, defaultValue = {}) => {
    try {
      return JSON.parse(jsonString || '{}')
    } catch (error) {
      return defaultValue
    }
  }

  const safeJsonParseArray = (jsonString, defaultValue = []) => {
    try {
      return JSON.parse(jsonString || '[]')
    } catch (error) {
      return defaultValue
    }
  }

  const handleApiCall = async (key, url, method = 'GET', data = null, isFile = false) => {
    // 새로운 요청 시작 시 모든 기존 응답과 로딩 상태 초기화
    setResponses({})
    setLoading({ [key]: true })
    try {
      const config = {
        method,
        url: `${currentApiUrl}${url}`,
        headers: isFile ? { 'Content-Type': 'multipart/form-data' } : { 'Content-Type': 'application/json' }
      }
      
      if (data) {
        if (isFile) {
          const formData = new FormData()
          formData.append('file', data)
          config.data = formData
        } else {
          // JSON 문자열이 포함된 데이터 처리
          const processedData = { ...data }
          if (processedData.meta_data && typeof processedData.meta_data === 'string') {
            processedData.meta_data = safeJsonParse(processedData.meta_data)
          }
          if (processedData.memos && typeof processedData.memos === 'string') {
            processedData.memos = safeJsonParseArray(processedData.memos)
          }
          config.data = processedData
        }
      }

      const response = await axios(config)
      setResponses(prev => ({
        ...prev,
        [key]: {
          status: response.status,
          data: response.data,
          success: true,
          timestamp: new Date().toLocaleString()
        }
      }))
    } catch (error) {
      setResponses(prev => ({
        ...prev,
        [key]: {
          status: error.response?.status || 'Error',
          data: error.response?.data || { message: error.message },
          success: false,
          timestamp: new Date().toLocaleString()
        }
      }))
    } finally {
      setLoading({ [key]: false })
    }
  }

  const handleFormChange = (category, field, value) => {
    if (category === 'groupId' || category === 'scanId') {
      // 최상위 레벨 필드 처리 (groupId, scanId)
      setFormData(prev => ({
        ...prev,
        [category]: value
      }))
    } else {
      // 중첩된 객체 필드 처리 (group, scan, file)
      setFormData(prev => ({
        ...prev,
        [category]: {
          ...prev[category],
          [field]: value
        }
      }))
    }
  }

  const renderResponse = (key) => {
    const response = responses[key]
    if (!response) return null

    return (
      <div className={`response ${response.success ? 'success' : 'error'}`}>
        <div className="response-header">
          <span className="status">Status: {response.status}</span>
          <span className="timestamp">{response.timestamp}</span>
        </div>
        <pre className="response-body">
          {JSON.stringify(response.data, null, 2)}
        </pre>
      </div>
    )
  }

  const ApiButton = ({ apiKey, url, method = 'GET', data = null, isFile = false, children }) => (
    <button
      className={`api-button ${method.toLowerCase()}`}
      onClick={() => handleApiCall(apiKey, url, method, data, isFile)}
      disabled={loading[apiKey]}
    >
      {loading[apiKey] ? '로딩...' : children}
    </button>
  )

  return (
    <div className="api-tester">
      <div className="api-config">
        <div className="config-group">
          <label>🌐 Backend API URL:</label>
          <input
            type="text"
            value={currentApiUrl}
            onChange={(e) => setCurrentApiUrl(e.target.value)}
            placeholder="http://192.168.1.100:8000"
            className="api-url-input"
          />
          <button
            className="api-button reset"
            onClick={() => setCurrentApiUrl(API_BASE_URL)}
          >
            초기화
          </button>
        </div>
      </div>
      
      <nav className="tabs">
        {[
          { key: 'health', label: '🏥 Health Check' },
          { key: 'groups', label: '👥 Groups' },
          { key: 'scans', label: '📷 Scans' },
          { key: 'upload', label: '📁 Upload' }
        ].map(tab => (
          <button
            key={tab.key}
            className={`tab ${activeTab === tab.key ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <div className="tab-content">
        {activeTab === 'health' && (
          <div className="section">
            <h2>Health Check APIs</h2>
            <div className="api-group">
              <h3>서비스 상태 확인</h3>
              <div className="api-actions">
                <ApiButton apiKey="health" url="/health">
                  GET /health
                </ApiButton>
                <ApiButton apiKey="ready" url="/ready">
                  GET /ready
                </ApiButton>
              </div>
              {renderResponse('health')}
              {renderResponse('ready')}
            </div>
          </div>
        )}

        {activeTab === 'groups' && (
          <div className="section">
            <h2>Groups Management</h2>
            
            <div className="api-group">
              <h3>그룹 API 테스트</h3>
              <div className="form-group">
                <label>그룹 ID:</label>
                <input
                  type="number"
                  value={formData.groupId}
                  onChange={(e) => handleFormChange('groupId', null, parseInt(e.target.value) || 1)}
                  min="1"
                  placeholder="그룹 ID를 입력하세요"
                />
              </div>
              <div className="form-group">
                <label>Meta Data (JSON):</label>
                <textarea
                  value={formData.group.meta_data}
                  onChange={(e) => handleFormChange('group', 'meta_data', e.target.value)}
                  placeholder='{"name": "테스트 그룹", "location": "서울", "type": "factory"}'
                />
              </div>
              <div className="api-actions">
                <ApiButton apiKey="groups-list" url="/api/v1/groups/?skip=0&limit=10">
                  GET /api/v1/groups/
                </ApiButton>
                <ApiButton apiKey="groups-get" url={`/api/v1/groups/${formData.groupId}`}>
                  GET /api/v1/groups/{formData.groupId}
                </ApiButton>
                <ApiButton apiKey="groups-scans" url={`/api/v1/groups/${formData.groupId}/scans`}>
                  GET /api/v1/groups/{formData.groupId}/scans
                </ApiButton>
                <ApiButton
                  apiKey="groups-create"
                  url="/api/v1/groups/"
                  method="POST"
                  data={{ meta_data: formData.group.meta_data }}
                >
                  POST /api/v1/groups/
                </ApiButton>
                <ApiButton
                  apiKey="groups-update"
                  url={`/api/v1/groups/${formData.groupId}`}
                  method="PUT"
                  data={{ meta_data: formData.group.meta_data }}
                >
                  PUT /api/v1/groups/{formData.groupId}
                </ApiButton>
                <ApiButton apiKey="groups-delete" url={`/api/v1/groups/${formData.groupId}`} method="DELETE">
                  DELETE /api/v1/groups/{formData.groupId}
                </ApiButton>
              </div>
              {renderResponse('groups-list')}
              {renderResponse('groups-get')}
              {renderResponse('groups-scans')}
              {renderResponse('groups-create')}
              {renderResponse('groups-update')}
              {renderResponse('groups-delete')}
            </div>
          </div>
        )}

        {activeTab === 'scans' && (
          <div className="section">
            <h2>Scans Management</h2>
            
            <div className="api-group">
              <h3>스캔 API 테스트</h3>
              <div className="form-group">
                <label>스캔 ID:</label>
                <input
                  type="number"
                  value={formData.scanId}
                  onChange={(e) => handleFormChange('scanId', null, parseInt(e.target.value) || 1)}
                  min="1"
                  placeholder="스캔 ID를 입력하세요"
                />
              </div>
              <div className="form-group">
                <label>Group ID:</label>
                <input
                  type="number"
                  value={formData.scan.group_id}
                  onChange={(e) => handleFormChange('scan', 'group_id', parseInt(e.target.value))}
                />
              </div>
              <div className="form-group">
                <label>Meta Data (JSON):</label>
                <textarea
                  value={formData.scan.meta_data}
                  onChange={(e) => handleFormChange('scan', 'meta_data', e.target.value)}
                  placeholder='{"scan_info": {"name": "테스트 스캔", "description": "설명"}}'
                />
              </div>
              <div className="form-group">
                <label>Status:</label>
                <select
                  value={formData.scan.status}
                  onChange={(e) => handleFormChange('scan', 'status', e.target.value)}
                >
                  <option value="UPLOADED">UPLOADED</option>
                  <option value="PROCESSING">PROCESSING</option>
                  <option value="COMPLETED">COMPLETED</option>
                  <option value="FAILED">FAILED</option>
                </select>
              </div>
              <div className="form-group">
                <label>Memos (JSON Array):</label>
                <textarea
                  rows="4"
                  value={formData.scan.memos}
                  onChange={(e) => handleFormChange('scan', 'memos', e.target.value)}
                  placeholder='[{"type": "text", "content": "메모 내용", "position": {"x": 1.25, "y": 1.5, "z": -3.4}}]'
                />
              </div>
              <div className="api-actions">
                <ApiButton apiKey="scans-list" url="/api/v1/scans/?skip=0&limit=10">
                  GET /api/v1/scans/
                </ApiButton>
                <ApiButton apiKey="scans-get" url={`/api/v1/scans/${formData.scanId}`}>
                  GET /api/v1/scans/{formData.scanId}
                </ApiButton>
                <ApiButton
                  apiKey="scans-create"
                  url="/api/v1/scans/"
                  method="POST"
                  data={{
                    group_id: formData.scan.group_id,
                    meta_data: formData.scan.meta_data,
                    status: formData.scan.status,
                    memos: formData.scan.memos
                  }}
                >
                  POST /api/v1/scans/
                </ApiButton>
                <ApiButton
                  apiKey="scans-update"
                  url={`/api/v1/scans/${formData.scanId}`}
                  method="PUT"
                  data={{
                    meta_data: formData.scan.meta_data,
                    status: formData.scan.status,
                    memos: formData.scan.memos
                  }}
                >
                  PUT /api/v1/scans/{formData.scanId}
                </ApiButton>
                <ApiButton apiKey="scans-delete" url={`/api/v1/scans/${formData.scanId}`} method="DELETE">
                  DELETE /api/v1/scans/{formData.scanId}
                </ApiButton>
              </div>
              {renderResponse('scans-list')}
              {renderResponse('scans-get')}
              {renderResponse('scans-create')}
              {renderResponse('scans-update')}
              {renderResponse('scans-delete')}
            </div>
          </div>
        )}

        {activeTab === 'upload' && (
          <div className="section">
            <h2>File Upload</h2>
            
            <div className="api-group">
              <h3>업로드 API 테스트</h3>
              <div className="form-group">
                <label>파일 선택:</label>
                <input
                  type="file"
                  onChange={(e) => handleFormChange('file', 'selected', e.target.files[0])}
                  accept=".ply,.obj,.stl"
                />
              </div>
              <div className="api-actions">
                <ApiButton apiKey="upload-status" url="/api/v1/upload/">
                  GET /api/v1/upload/
                </ApiButton>
                <ApiButton
                  apiKey="upload-file"
                  url="/api/v1/upload/"
                  method="POST"
                  data={formData.file?.selected}
                  isFile={true}
                >
                  POST /api/v1/upload/
                </ApiButton>
              </div>
              {renderResponse('upload-status')}
              {renderResponse('upload-file')}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

export default ApiTester

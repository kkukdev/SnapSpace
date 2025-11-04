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
  const [uploadProgress, setUploadProgress] = useState({})
  const [currentApiUrl, setCurrentApiUrl] = useState(API_BASE_URL)
  const [formData, setFormData] = useState({
    group: { meta_data: '{}' },
    scan: { group_id: 1, meta_data: '{}', status: 'UPLOADED', memos: '[]' },
    file: { selected: null, groupName: '' },
    groupId: 1,
    scanId: 1,
    groupsList: []
  })

  const safeJsonParse = (jsonString, defaultValue = {}) => {
    try {
      return JSON.parse(jsonString || '{}')
    } catch {
      return defaultValue
    }
  }

  const safeJsonParseArray = (jsonString, defaultValue = []) => {
    try {
      return JSON.parse(jsonString || '[]')
    } catch {
      return defaultValue
    }
  }

  const handleApiCall = async (key, url, method = 'GET', data = null, isFile = false) => {
    // 새로운 요청 시작 시 모든 기존 응답과 로딩 상태 초기화
    setResponses({})
    setLoading({ [key]: true })
    setUploadProgress({})
    
    try {
      // 파일 업로드이고 group_name이 있는 경우, 먼저 그룹 처리
      let groupId = null
      if (isFile && data && data.groupName) {
        try {
          // 기존 그룹 목록 조회
          const groupsResponse = await axios.get(`${currentApiUrl}/api/v1/groups/?skip=0&limit=1000`)
          if (groupsResponse.data?.success && groupsResponse.data?.data?.groups) {
            // group_name과 일치하는 그룹 찾기 (meta_data.name으로 검색)
            const matchingGroup = groupsResponse.data.data.groups.find(
              group => group.meta_data?.name === data.groupName
            )
            
            if (matchingGroup) {
              groupId = matchingGroup.group_id
            } else {
              // 그룹이 없으면 생성
              const createResponse = await axios.post(
                `${currentApiUrl}/api/v1/groups/`,
                { meta_data: { name: data.groupName } }
              )
              if (createResponse.data?.success && createResponse.data?.data) {
                groupId = createResponse.data.data.group_id
              }
            }
          }
        } catch (error) {
          console.error('그룹 처리 중 오류:', error)
          // 그룹 처리 실패해도 파일 업로드는 계속 진행
        }
      }
      
      const config = {
        method,
        url: `${currentApiUrl}${url}`,
        headers: isFile ? { 'Content-Type': 'multipart/form-data' } : { 'Content-Type': 'application/json' }
      }
      
      // 파일 업로드 시 진행률 콜백 추가
      if (isFile) {
        config.onUploadProgress = (progressEvent) => {
          const percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total)
          setUploadProgress({
            [key]: {
              loaded: progressEvent.loaded,
              total: progressEvent.total,
              percentage: percentCompleted
            }
          })
        }
      }
      
      if (data) {
        if (isFile) {
          const formData = new FormData()
          formData.append('file', data.file || data)
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
      
      // 파일 업로드 성공 후 groupId가 있으면 scan 생성
      if (isFile && key === 'upload-file' && groupId && response.data?.success) {
        try {
          const filePath = response.data?.data?.file_path
          if (filePath) {
            const scanData = {
              group_id: groupId,
              meta_data: {
                original_filename: response.data?.data?.original_filename,
                saved_filename: response.data?.data?.saved_filename,
                file_size: response.data?.data?.file_size,
                upload_timestamp: new Date().toISOString()
              },
              status: 'UPLOADED',
              file_path: filePath,
              memos: null
            }
            
            await axios.post(`${currentApiUrl}/api/v1/scans/`, scanData)
            // scan 생성 성공은 조용히 처리 (응답에 추가 정보 포함)
            response.data.data.group_id = groupId
            response.data.data.group_name = data.groupName
          }
        } catch (error) {
          console.error('Scan 생성 중 오류:', error)
          // scan 생성 실패해도 업로드는 성공으로 처리
        }
      }
      
      // 업로드 완료 시 진행률 100%로 설정
      if (isFile) {
        setUploadProgress({
          [key]: {
            loaded: response.data?.data?.file_size || 0,
            total: response.data?.data?.file_size || 0,
            percentage: 100
          }
        })
      }
      
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
      // 업로드 실패 시 진행률 초기화
      if (isFile) {
        setUploadProgress({})
      }
      
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

  const formatFileSize = (bytes) => {
    if (!bytes) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  const renderProgressBar = (apiKey) => {
    const progress = uploadProgress[apiKey]
    if (!progress) return null

    return (
      <div className="upload-progress">
        <div className="progress-info">
          <span className="progress-text">
            업로드 중... {progress.percentage}%
          </span>
          <span className="progress-size">
            {formatFileSize(progress.loaded)} / {formatFileSize(progress.total)}
          </span>
        </div>
        <div className="progress-bar">
          <div 
            className="progress-fill" 
            style={{ width: `${progress.percentage}%` }}
          />
        </div>
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
                  accept=".ply,.obj,.stl,.zip"
                />
              </div>
              <div className="form-group">
                <label>그룹 이름:</label>
                <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                  <select
                    value={formData.file?.groupName || ''}
                    onChange={(e) => {
                      if (e.target.value) {
                        handleFormChange('file', 'groupName', e.target.value)
                      }
                    }}
                    style={{ flex: 1, padding: '8px' }}
                  >
                    <option value="">새 그룹 생성 (아래에 이름 입력)</option>
                    {formData.groupsList.map((group) => {
                      const name = group.meta_data?.name || `Group ${group.group_id}`
                      return (
                        <option key={group.group_id} value={name}>
                          {name} (ID: {group.group_id})
                        </option>
                      )
                    })}
                  </select>
                  <button
                    className="api-button"
                    onClick={async () => {
                      try {
                        const response = await axios.get(`${currentApiUrl}/api/v1/groups/?skip=0&limit=100`)
                        if (response.data?.success && response.data?.data?.groups) {
                          setFormData(prev => ({
                            ...prev,
                            groupsList: response.data.data.groups
                          }))
                        }
                      } catch (error) {
                        console.error('그룹 목록 조회 실패:', error)
                      }
                    }}
                    style={{ padding: '8px 16px' }}
                  >
                    그룹 목록 새로고침
                  </button>
                </div>
                <input
                  type="text"
                  value={formData.file?.groupName || ''}
                  onChange={(e) => handleFormChange('file', 'groupName', e.target.value)}
                  placeholder="그룹 이름을 입력하세요 (기존 그룹 선택 또는 새 그룹 이름)"
                  style={{ marginTop: '8px', width: '100%', padding: '8px' }}
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
                  data={{ file: formData.file?.selected, groupName: formData.file?.groupName }}
                  isFile={true}
                >
                  POST /api/v1/upload/
                </ApiButton>
              </div>
              {renderProgressBar('upload-file')}
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

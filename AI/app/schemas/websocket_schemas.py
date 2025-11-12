from pydantic import BaseModel
from typing import Optional, List, Dict, Any
from enum import Enum
from datetime import datetime

class WSMessageType(str, Enum):
    FILE_LIST = "file_list"
    PROCESSING_START = "processing_start"
    PROCESSING_PROGRESS = "processing_progress"
    PROCESSING_COMPLETE = "processing_complete"
    PROCESSING_ERROR = "processing_error"
    HEARTBEAT = "heartbeat"
    CONNECTION_STATUS = "connection_status"

class FileProcessingRequest(BaseModel):
    scan_id: int
    original_file_path: str
    group_id: str
    model_type: Optional[str] = None
    metadata: Dict[str, Any]

class FileListMessage(BaseModel):
    type: WSMessageType = WSMessageType.FILE_LIST
    files: List[FileProcessingRequest]

class ProcessingStatusUpdate(BaseModel):
    scan_id: int
    status: str  # "PROCESSING", "COMPLETED", "ERROR"
    progress: Optional[int] = None  # 0-100
    error_message: Optional[str] = None
    output_file_path: Optional[str] = None
    timestamp: datetime = datetime.now()

class ProcessingStartMessage(BaseModel):
    type: WSMessageType = WSMessageType.PROCESSING_START
    scan_id: int

class ProcessingProgressMessage(BaseModel):
    type: WSMessageType = WSMessageType.PROCESSING_PROGRESS
    scan_id: int
    progress: int

class ProcessingCompleteMessage(BaseModel):
    type: WSMessageType = WSMessageType.PROCESSING_COMPLETE
    scan_id: int
    output_file_path: str

class ProcessingErrorMessage(BaseModel):
    type: WSMessageType = WSMessageType.PROCESSING_ERROR
    scan_id: int
    error_message: str

class HeartbeatMessage(BaseModel):
    type: WSMessageType = WSMessageType.HEARTBEAT
    timestamp: datetime = datetime.now()

class ConnectionStatusMessage(BaseModel):
    type: WSMessageType = WSMessageType.CONNECTION_STATUS
    status: str  # "connected", "disconnected", "reconnecting"
    timestamp: datetime = datetime.now()
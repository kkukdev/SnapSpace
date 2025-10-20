from fastapi import FastAPI

app = FastAPI(
    docs_url="/api/v1/docs",
    redoc_url="/api/v1/redoc",
    openapi_url="/api/v1/openapi.json"
)


@app.get("/api/v1")
async def root():
    return {"message": "Hello World"}
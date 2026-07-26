from fastapi import APIRouter

router = APIRouter()

@router.get("/health", tags=["Health"])
async def health_check():
    """
    Lightweight health endpoint.
    Returns OK if the FastAPI app (and thus Uvicorn) is up.
    """
    return {"status": "ok", "service": "uvicorn running"}

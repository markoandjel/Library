from fastapi import APIRouter

# Import all route modules
from app.routes import (
    routes_health,
    routes_authors,
    routes_publisher,
    routes_letters,
    routes_categories,
    routes_languages,
    routes_cabinets,
    routes_shelfs,
    routes_books
)

# Create a single unified router
api_router = APIRouter()

# Include all route modules
api_router.include_router(routes_health.router)
api_router.include_router(routes_authors.router)
api_router.include_router(routes_publisher.router)
api_router.include_router(routes_letters.router)
api_router.include_router(routes_categories.router)
api_router.include_router(routes_languages.router)
api_router.include_router(routes_cabinets.router)
api_router.include_router(routes_shelfs.router)
api_router.include_router(routes_books.router)




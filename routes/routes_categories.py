# app/routes/routes_categories.py

from typing import List
from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.category_schema import CategoryCreate, CategoryUpdate, CategoryOut

router = APIRouter(prefix="/categories", tags=["categories"])


# ---------- Helpers ----------

def _get_category_or_404(cursor, category_id: int) -> dict:
    cursor.execute(
        "SELECT id, naziv, opis FROM kategorija WHERE id = %s",
        (category_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Category with id={category_id} not found",
        )
    return row


# ---------- Routes ----------

@router.get("/", response_model=List[CategoryOut])
def list_categories(cursor=Depends(get_cursor)):
    """
    List all categories ordered by name.
    """
    cursor.execute("SELECT id, naziv, opis FROM kategorija ORDER BY id;")
    return cursor.fetchall()


@router.get("/{category_id}", response_model=CategoryOut)
def get_category(category_id: int, cursor=Depends(get_cursor)):
    """
    Get a single category by ID.
    """
    return _get_category_or_404(cursor, category_id)


@router.post(
    "/",
    response_model=CategoryOut,
    status_code=status.HTTP_201_CREATED,
)
def create_category(payload: CategoryCreate, cursor=Depends(get_cursor)):
    """
    Create a new category.
    """
    # optional: prevent duplicate names (case-insensitive)
    cursor.execute(
        "SELECT id FROM kategorija WHERE LOWER(naziv) = LOWER(%s)",
        (payload.naziv,),
    )
    if cursor.fetchone():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Category with this name already exists.",
        )

    cursor.execute(
        "INSERT INTO kategorija (naziv, opis) VALUES (%s, %s)",
        (payload.naziv, payload.opis),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        "SELECT id, naziv, opis FROM kategorija WHERE id = %s",
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{category_id}", response_model=CategoryOut)
def update_category(
    category_id: int,
    payload: CategoryCreate,
    cursor=Depends(get_cursor),
):
    """
    Full update of a category.
    """
    _get_category_or_404(cursor, category_id)

    cursor.execute(
        "UPDATE kategorija SET naziv = %s, opis = %s WHERE id = %s",
        (payload.naziv, payload.opis, category_id),
    )

    cursor.execute(
        "SELECT id, naziv, opis FROM kategorija WHERE id = %s",
        (category_id,),
    )
    return cursor.fetchone()


@router.patch("/{category_id}", response_model=CategoryOut)
def patch_category(
    category_id: int,
    payload: CategoryUpdate,
    cursor=Depends(get_cursor),
):
    """
    Partial update of a category.
    """
    existing = _get_category_or_404(cursor, category_id)

    new_naziv = payload.naziv if payload.naziv is not None else existing["naziv"]
    new_opis = payload.opis if payload.opis is not None else existing["opis"]

    cursor.execute(
        "UPDATE kategorija SET naziv = %s, opis = %s WHERE id = %s",
        (new_naziv, new_opis, category_id),
    )

    cursor.execute(
        "SELECT id, naziv, opis FROM kategorija WHERE id = %s",
        (category_id,),
    )
    return cursor.fetchone()


@router.delete("/{category_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_category(category_id: int, cursor=Depends(get_cursor)):
    """
    Delete a category by ID.
    """
    _get_category_or_404(cursor, category_id)
    cursor.execute("DELETE FROM kategorija WHERE id = %s", (category_id,))

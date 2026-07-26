from typing import List
from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.shelf_schema import ShelfCreate, ShelfUpdate, ShelfOut

router = APIRouter(prefix="/shelves", tags=["shelves"])


# ---------- Helpers ----------

def _get_shelf_or_404(cursor, shelf_id: int) -> dict:
    cursor.execute(
        """
        SELECT id, x, y, orman_id
        FROM polica
        WHERE id = %s
        """,
        (shelf_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Shelf with id={shelf_id} not found",
        )
    return row


def _ensure_cabinet_exists(cursor, orman_id: int) -> None:
    cursor.execute(
        "SELECT id FROM orman WHERE id = %s",
        (orman_id,),
    )
    if cursor.fetchone() is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Cabinet with id={orman_id} does not exist.",
        )


# ---------- Routes ----------

@router.get("/", response_model=List[ShelfOut])
def list_shelves(cursor=Depends(get_cursor)):
    """
    List all shelves.
    """
    cursor.execute(
        """
        SELECT id, x, y, orman_id
        FROM polica
        ORDER BY orman_id, y, x
        """
    )
    return cursor.fetchall()


@router.get("/{shelf_id}", response_model=ShelfOut)
def get_shelf(shelf_id: int, cursor=Depends(get_cursor)):
    """
    Get a shelf by ID.
    """
    return _get_shelf_or_404(cursor, shelf_id)


@router.post(
    "/",
    response_model=ShelfOut,
    status_code=status.HTTP_201_CREATED,
)
def create_shelf(payload: ShelfCreate, cursor=Depends(get_cursor)):
    """
    Create a new shelf.
    """
    _ensure_cabinet_exists(cursor, payload.orman_id)

    cursor.execute(
        """
        INSERT INTO polica (x, y, orman_id)
        VALUES (%s, %s, %s)
        """,
        (payload.x, payload.y, payload.orman_id),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        """
        SELECT id, x, y, orman_id
        FROM polica
        WHERE id = %s
        """,
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{shelf_id}", response_model=ShelfOut)
def update_shelf(
    shelf_id: int,
    payload: ShelfCreate,
    cursor=Depends(get_cursor),
):
    """
    Full update of a shelf.
    """
    _get_shelf_or_404(cursor, shelf_id)
    _ensure_cabinet_exists(cursor, payload.orman_id)

    cursor.execute(
        """
        UPDATE polica
        SET x = %s,
            y = %s,
            orman_id = %s
        WHERE id = %s
        """,
        (payload.x, payload.y, payload.orman_id, shelf_id),
    )

    cursor.execute(
        """
        SELECT id, x, y, orman_id
        FROM polica
        WHERE id = %s
        """,
        (shelf_id,),
    )
    return cursor.fetchone()


@router.patch("/{shelf_id}", response_model=ShelfOut)
def patch_shelf(
    shelf_id: int,
    payload: ShelfUpdate,
    cursor=Depends(get_cursor),
):
    """
    Partial update of a shelf.
    """
    existing = _get_shelf_or_404(cursor, shelf_id)

    new_x = payload.x if payload.x is not None else existing["x"]
    new_y = payload.y if payload.y is not None else existing["y"]
    new_orman_id = (
        payload.orman_id if payload.orman_id is not None else existing["orman_id"]
    )

    _ensure_cabinet_exists(cursor, new_orman_id)

    cursor.execute(
        """
        UPDATE polica
        SET x = %s,
            y = %s,
            orman_id = %s
        WHERE id = %s
        """,
        (new_x, new_y, new_orman_id, shelf_id),
    )

    cursor.execute(
        """
        SELECT id, x, y, orman_id
        FROM polica
        WHERE id = %s
        """,
        (shelf_id,),
    )
    return cursor.fetchone()


@router.delete("/{shelf_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_shelf(shelf_id: int, cursor=Depends(get_cursor)):
    """
    Delete a shelf by ID.
    """
    _get_shelf_or_404(cursor, shelf_id)
    cursor.execute(
        "DELETE FROM polica WHERE id = %s",
        (shelf_id,),
    )

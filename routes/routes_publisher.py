from typing import List
from fastapi import APIRouter, Depends, HTTPException, status
from app.db.session import get_cursor
from app.schemas.publisher_schema import PublisherCreate, PublisherUpdate, PublisherOut

router = APIRouter(prefix="/publishers", tags=["publishers"])


# ---------- Helpers ----------

def _get_publisher_or_404(cursor, publisher_id: int) -> dict:
    cursor.execute(
        "SELECT id, naziv FROM izdavac WHERE id = %s",
        (publisher_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Publisher with id={publisher_id} not found",
        )
    return row


# ---------- Routes ----------

@router.get("/", response_model=List[PublisherOut])
def list_publishers(cursor=Depends(get_cursor)):
    """
    List all publishers ordered by name.
    """
    cursor.execute("SELECT id, naziv FROM izdavac ORDER BY id;")
    return cursor.fetchall()


@router.get("/{publisher_id}", response_model=PublisherOut)
def get_publisher(publisher_id: int, cursor=Depends(get_cursor)):
    """
    Get a single publisher by ID.
    """
    return _get_publisher_or_404(cursor, publisher_id)


@router.post(
    "/",
    response_model=PublisherOut,
    status_code=status.HTTP_201_CREATED,
)
def create_publisher(payload: PublisherCreate, cursor=Depends(get_cursor)):
    """
    Create a new publisher.
    """
    # Optional: check for duplicates (case-insensitive)
    cursor.execute(
        "SELECT id FROM izdavac WHERE LOWER(naziv) = LOWER(%s)",
        (payload.naziv,),
    )
    if cursor.fetchone():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Publisher with this name already exists.",
        )

    cursor.execute(
        "INSERT INTO izdavac (naziv) VALUES (%s)",
        (payload.naziv,),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        "SELECT id, naziv FROM izdavac WHERE id = %s",
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{publisher_id}", response_model=PublisherOut)
def update_publisher(publisher_id: int, payload: PublisherCreate, cursor=Depends(get_cursor)):
    """
    Full update of a publisher.
    """
    _get_publisher_or_404(cursor, publisher_id)

    cursor.execute(
        "UPDATE izdavac SET naziv = %s WHERE id = %s",
        (payload.naziv, publisher_id),
    )

    cursor.execute(
        "SELECT id, naziv FROM izdavac WHERE id = %s",
        (publisher_id,),
    )
    return cursor.fetchone()


@router.patch("/{publisher_id}", response_model=PublisherOut)
def patch_publisher(publisher_id: int, payload: PublisherUpdate, cursor=Depends(get_cursor)):
    """
    Partial update of a publisher.
    """
    existing = _get_publisher_or_404(cursor, publisher_id)
    new_name = payload.naziv if payload.naziv is not None else existing["naziv"]

    cursor.execute(
        "UPDATE izdavac SET naziv = %s WHERE id = %s",
        (new_name, publisher_id),
    )

    cursor.execute(
        "SELECT id, naziv FROM izdavac WHERE id = %s",
        (publisher_id,),
    )
    return cursor.fetchone()


@router.delete("/{publisher_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_publisher(publisher_id: int, cursor=Depends(get_cursor)):
    """
    Delete a publisher by ID.
    """
    _get_publisher_or_404(cursor, publisher_id)
    cursor.execute("DELETE FROM izdavac WHERE id = %s", (publisher_id,))
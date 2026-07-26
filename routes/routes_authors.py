from typing import List

from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.author_schema import AuthorCreate, AuthorUpdate, AuthorOut

router = APIRouter(prefix="/authors", tags=["authors"])


# ---------- Helpers ----------

def _get_author_or_404(cursor, author_id: int) -> dict:
    cursor.execute(
        "SELECT id, ime FROM autor WHERE id = %s",
        (author_id,),
    )
    row = cursor.fetchone()

    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Author with id={author_id} not found",
        )

    return row


# ---------- Routes ----------

@router.get("/", response_model=List[AuthorOut])
def list_authors(cursor=Depends(get_cursor)):
    """
    List all authors ordered by name.
    """
    cursor.execute("SELECT id, ime FROM autor ORDER BY id;")
    return cursor.fetchall()


@router.get("/{author_id}", response_model=AuthorOut)
def get_author(author_id: int, cursor=Depends(get_cursor)):
    """
    Get a single author by ID.
    """
    return _get_author_or_404(cursor, author_id)


@router.post(
    "/",
    response_model=AuthorOut,
    status_code=status.HTTP_201_CREATED,
)
def create_author(payload: AuthorCreate, cursor=Depends(get_cursor)):
    """
    Create a new author.
    """
    # Optional: prevent duplicates by name (case-insensitive)
    cursor.execute(
        "SELECT id FROM autor WHERE LOWER(ime) = LOWER(%s)",
        (payload.ime,),
    )
    existing = cursor.fetchone()
    if existing:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Author with this name already exists.",
        )

    cursor.execute(
        "INSERT INTO autor (ime) VALUES (%s)",
        (payload.ime,),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        "SELECT id, ime FROM autor WHERE id = %s",
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{author_id}", response_model=AuthorOut)
def update_author(author_id: int, payload: AuthorCreate, cursor=Depends(get_cursor)):
    """
    Full update of an author (overwrite name).
    """
    _get_author_or_404(cursor, author_id)

    cursor.execute(
        "UPDATE autor SET ime = %s WHERE id = %s",
        (payload.ime, author_id),
    )

    cursor.execute(
        "SELECT id, ime FROM autor WHERE id = %s",
        (author_id,),
    )
    return cursor.fetchone()


@router.patch("/{author_id}", response_model=AuthorOut)
def patch_author(author_id: int, payload: AuthorUpdate, cursor=Depends(get_cursor)):
    """
    Partial update of an author.
    """
    existing = _get_author_or_404(cursor, author_id)

    new_name = payload.ime if payload.ime is not None else existing["ime"]

    cursor.execute(
        "UPDATE autor SET ime = %s WHERE id = %s",
        (new_name, author_id),
    )

    cursor.execute(
        "SELECT id, ime FROM autor WHERE id = %s",
        (author_id,),
    )
    return cursor.fetchone()


@router.delete("/{author_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_author(author_id: int, cursor=Depends(get_cursor)):
    """
    Delete an author by ID.
    """
    _get_author_or_404(cursor, author_id)

    cursor.execute(
        "DELETE FROM autor WHERE id = %s",
        (author_id,),
    )
    # get_cursor handles commit/close; 204 -> no body

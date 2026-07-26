# app/routes/routes_languages.py

from typing import List
from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.language_schema import LanguageCreate, LanguageUpdate, LanguageOut

router = APIRouter(prefix="/languages", tags=["languages"])


# ---------- Helpers ----------

def _get_language_or_404(cursor, language_id: int) -> dict:
    cursor.execute(
        "SELECT id, naziv FROM jezik WHERE id = %s",
        (language_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Language with id={language_id} not found",
        )
    return row


# ---------- Routes ----------

@router.get("/", response_model=List[LanguageOut])
def list_languages(cursor=Depends(get_cursor)):
    """
    List all languages ordered by name.
    """
    cursor.execute("SELECT id, naziv FROM jezik ORDER BY id;")
    return cursor.fetchall()


@router.get("/{language_id}", response_model=LanguageOut)
def get_language(language_id: int, cursor=Depends(get_cursor)):
    """
    Get a single language by ID.
    """
    return _get_language_or_404(cursor, language_id)


@router.post(
    "/",
    response_model=LanguageOut,
    status_code=status.HTTP_201_CREATED,
)
def create_language(payload: LanguageCreate, cursor=Depends(get_cursor)):
    """
    Create a new language.
    """
    cursor.execute(
        "SELECT id FROM jezik WHERE LOWER(naziv) = LOWER(%s)",
        (payload.naziv,),
    )
    if cursor.fetchone():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Language with this name already exists.",
        )

    cursor.execute(
        "INSERT INTO jezik (naziv) VALUES (%s)",
        (payload.naziv,),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        "SELECT id, naziv FROM jezik WHERE id = %s",
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{language_id}", response_model=LanguageOut)
def update_language(
    language_id: int,
    payload: LanguageCreate,
    cursor=Depends(get_cursor),
):
    """
    Full update of a language.
    """
    _get_language_or_404(cursor, language_id)

    cursor.execute(
        "UPDATE jezik SET naziv = %s WHERE id = %s",
        (payload.naziv, language_id),
    )

    cursor.execute(
        "SELECT id, naziv FROM jezik WHERE id = %s",
        (language_id,),
    )
    return cursor.fetchone()


@router.patch("/{language_id}", response_model=LanguageOut)
def patch_language(
    language_id: int,
    payload: LanguageUpdate,
    cursor=Depends(get_cursor),
):
    """
    Partial update of a language.
    """
    existing = _get_language_or_404(cursor, language_id)
    new_naziv = payload.naziv if payload.naziv is not None else existing["naziv"]

    cursor.execute(
        "UPDATE jezik SET naziv = %s WHERE id = %s",
        (new_naziv, language_id),
    )

    cursor.execute(
        "SELECT id, naziv FROM jezik WHERE id = %s",
        (language_id,),
    )
    return cursor.fetchone()


@router.delete("/{language_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_language(language_id: int, cursor=Depends(get_cursor)):
    """
    Delete a language by ID.
    """
    _get_language_or_404(cursor, language_id)
    cursor.execute("DELETE FROM jezik WHERE id = %s", (language_id,))

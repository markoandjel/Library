from typing import List
from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.letter_schema import LetterCreate, LetterUpdate, LetterOut

router = APIRouter(prefix="/letters", tags=["letters"])


# ---------- Helpers ----------

def _get_letter_or_404(cursor, letter_id: int) -> dict:
    # Map naziv (DB) -> pismo (API)
    cursor.execute(
        "SELECT id, naziv AS pismo FROM pismo WHERE id = %s",
        (letter_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Letter with id={letter_id} not found",
        )
    return row


# ---------- Routes ----------

@router.get("/", response_model=List[LetterOut])
def list_letters(cursor=Depends(get_cursor)):
    """
    List all letters (scripts) ordered by name.
    """
    # naziv AS pismo so it matches LetterOut
    cursor.execute("SELECT id, naziv AS pismo FROM pismo ORDER BY id;")
    return cursor.fetchall()


@router.get("/{letter_id}", response_model=LetterOut)
def get_letter(letter_id: int, cursor=Depends(get_cursor)):
    """
    Get a single letter by ID.
    """
    return _get_letter_or_404(cursor, letter_id)


@router.post(
    "/",
    response_model=LetterOut,
    status_code=status.HTTP_201_CREATED,
)
def create_letter(payload: LetterCreate, cursor=Depends(get_cursor)):
    """
    Create a new letter.
    """
    # payload.pismo -> DB column naziv
    cursor.execute(
        "SELECT id FROM pismo WHERE LOWER(naziv) = LOWER(%s)",
        (payload.pismo,),
    )
    if cursor.fetchone():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Letter with this name already exists.",
        )

    cursor.execute(
        "INSERT INTO pismo (naziv) VALUES (%s)",
        (payload.pismo,),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        "SELECT id, naziv AS pismo FROM pismo WHERE id = %s",
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{letter_id}", response_model=LetterOut)
def update_letter(letter_id: int, payload: LetterCreate, cursor=Depends(get_cursor)):
    """
    Full update of a letter.
    """
    _get_letter_or_404(cursor, letter_id)

    cursor.execute(
        "UPDATE pismo SET naziv = %s WHERE id = %s",
        (payload.pismo, letter_id),
    )

    cursor.execute(
        "SELECT id, naziv AS pismo FROM pismo WHERE id = %s",
        (letter_id,),
    )
    return cursor.fetchone()


@router.patch("/{letter_id}", response_model=LetterOut)
def patch_letter(letter_id: int, payload: LetterUpdate, cursor=Depends(get_cursor)):
    """
    Partial update of a letter.
    """
    existing = _get_letter_or_404(cursor, letter_id)
    new_value = payload.pismo if payload.pismo is not None else existing["pismo"]

    cursor.execute(
        "UPDATE pismo SET naziv = %s WHERE id = %s",
        (new_value, letter_id),
    )

    cursor.execute(
        "SELECT id, naziv AS pismo FROM pismo WHERE id = %s",
        (letter_id,),
    )
    return cursor.fetchone()


@router.delete("/{letter_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_letter(letter_id: int, cursor=Depends(get_cursor)):
    """
    Delete a letter by ID.
    """
    _get_letter_or_404(cursor, letter_id)
    cursor.execute("DELETE FROM pismo WHERE id = %s", (letter_id,))

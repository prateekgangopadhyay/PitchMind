#!/usr/bin/env python3
"""Prepare the approved tactical cards as bounded Azure AI Search source documents.

This is an offline transform only. It does not crawl, call Azure, embed, or upload content.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = ROOT / "src/PitchMind.AgentWorker/Agents/football-tactical-knowledge-base.v1.json"
DEFAULT_CATALOGUE = ROOT / "docs/ai/fifa-training-centre-article-catalog.v1.json"
DEFAULT_OUTPUT = ROOT / "data/ai-search/football-tactical-knowledge-seed.jsonl"
MAX_CHARS = 4_000
TOPIC_MAP = {
    "team-shape": ("team-movement", "formation-and-phase-shape"),
    "formation": ("formation-and-phase-shape", "formation-matchups-and-adjustments"),
    "phases": ("phase-by-phase-playbooks",),
    "phase": ("phase-by-phase-playbooks",),
    "2v1": ("attacking-2v1",),
    "overloads": ("attacking-2v1",),
    "pressing": ("press-and-block-dynamics", "pressing-triggers-and-escape-routes"),
    "defensive-shape": ("press-and-block-dynamics", "formation-and-phase-shape"),
    "transition": ("transitions-rest-defence", "transition-analysis-layer"),
    "rest-defence": ("transitions-rest-defence",),
    "set-piece": ("set-piece-restarts", "set-piece-roles-and-second-phases"),
    "goalkeeper": ("goalkeeper-team-integration",),
    "substitution": ("substitution-analysis", "late-match-and-unusual-game-states"),
    "late-match": ("late-match-and-unusual-game-states", "late-game-fouls"),
    "foul": ("late-game-fouls", "late-match-and-unusual-game-states"),
    "chance": ("chance-patterns-and-prevention", "final-third-attack"),
    "final-third": ("final-third-attack",),
    "player": ("player-role-fit-and-complementarity",),
    "role": ("player-role-fit-and-complementarity",),
    "build-up": ("in-possession-progression", "goalkeeper-team-integration"),
    "progression": ("in-possession-progression",),
    "match-analysis": ("tactical-vocabulary-metrics-evidence", "phase-by-phase-playbooks"),
}


def chunk_card(card: dict) -> list[tuple[str, str]]:
    sections = [("overview", card["promptSummary"])]
    sections.extend((key, f"{key}: " + " ".join(card.get(key, [])))
                    for key in ("mechanisms", "requiredEvidence", "analysisSteps", "limitations")
                    if card.get(key))
    chunks: list[str] = []
    current = ""
    for _, text in sections:
        for paragraph in text.split(". "):
            paragraph = paragraph.strip()
            if not paragraph:
                continue
            candidate = f"{current} {paragraph}".strip()
            if len(candidate) > MAX_CHARS and current:
                chunks.append(current)
                current = paragraph
            else:
                current = candidate
            if len(current) > MAX_CHARS:
                raise ValueError(f"A paragraph in card {card.get('id')} exceeds the {MAX_CHARS}-character chunk budget")
    if current:
        chunks.append(current)
    return [(f"part-{index:03d}", chunk) for index, chunk in enumerate(chunks, start=1)]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--catalogue", type=Path, default=DEFAULT_CATALOGUE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    document = json.loads(args.input.read_text(encoding="utf-8"))
    if document.get("schemaVersion") != "1.0.0" or not isinstance(document.get("cards"), list):
        raise ValueError("Unsupported tactical knowledge base schema")

    ids: set[str] = set()
    records = []
    for card in document["cards"]:
        card_id = card.get("id", "")
        if not card_id or card_id in ids:
            raise ValueError(f"Missing or duplicate knowledge card id: {card_id!r}")
        ids.add(card_id)
        for part, content in chunk_card(card):
            records.append({
                "id": f"{card_id}-{part}",
                "topicId": card_id,
                "title": card["topic"],
                "content": content,
                "sourceUri": None,
                "sourceType": "pitchmind-curated-knowledge-card",
                "version": document["schemaVersion"],
            })

    catalogue = json.loads(args.catalogue.read_text(encoding="utf-8"))
    if catalogue.get("schemaVersion") != "1.0.0" or not isinstance(catalogue.get("entries"), list):
        raise ValueError("Unsupported football source catalogue schema")
    source_ids: set[str] = set()
    for entry in catalogue["entries"]:
        source_id = entry.get("id", "")
        source_url = entry.get("url", "")
        if not source_id or source_id in source_ids or not re.fullmatch(r"[a-z0-9-]+", source_id):
            raise ValueError(f"Missing, unsafe or duplicate source catalogue id: {source_id!r}")
        if len(source_id) > 64 or urlparse(source_url).scheme != "https" or not entry.get("abstract"):
            raise ValueError(f"Source catalogue entry {source_id} has invalid URL or empty abstract")
        source_ids.add(source_id)
        destinations = sorted({target for topic in entry.get("topics", [])
                               for key, mapped in TOPIC_MAP.items() if key in topic.lower()
                               for target in mapped})
        for topic_id in destinations:
            records.append({
                "id": f"source-{source_id}-{topic_id}",
                "topicId": topic_id,
                "title": entry["title"],
                "content": (f"Reviewed source catalogue abstract: {entry['abstract']} "
                            "This source is for general football education; it is not evidence about a selected match."),
                "sourceUri": source_url,
                "sourceType": entry["sourceType"],
                "version": f"catalogue-{catalogue['schemaVersion']}-reviewed-{entry['reviewedAt']}",
            })

    document_ids = [record["id"] for record in records]
    if len(document_ids) != len(set(document_ids)) or any(len(value) > 128 or not re.fullmatch(r"[a-z0-9-]+", value)
                                                           for value in document_ids):
        raise ValueError("Prepared corpus contains an unsafe or duplicate document ID")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        for record in records:
            output.write(json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n")
    print(f"Prepared {len(records)} bounded knowledge documents from {len(document['cards'])} curated cards and {len(catalogue['entries'])} reviewed source references at {args.output}")


if __name__ == "__main__":
    main()

"""Minimal OpenRouter client mirroring the request shapes used by Unity LLMService.

Text calls → POST /chat/completions, standard messages array.
Image calls → POST /chat/completions with modalities=["image","text"] and content
              parts containing any reference images followed by a text part.
"""

from __future__ import annotations

import base64
import io
import os
import time
from dataclasses import dataclass
from typing import Optional

import requests
from PIL import Image

ENDPOINT = "https://openrouter.ai/api/v1/chat/completions"


@dataclass
class Client:
    api_key: str
    text_model: str = "google/gemini-2.5-flash"
    image_model: str = "google/gemini-2.5-flash-image"
    timeout: int = 180
    referer: str = "https://github.com/google-deepmind/antigravity"
    title: str = "DNDLLM-testbed"

    @classmethod
    def from_env(cls, **kwargs) -> "Client":
        key = os.environ.get("OPENROUTER_API_KEY")
        if not key:
            raise RuntimeError(
                "OPENROUTER_API_KEY not set. Export it or load via --env-file."
            )
        return cls(api_key=key, **kwargs)

    def _headers(self):
        return {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
            "HTTP-Referer": self.referer,
            "X-Title": self.title,
        }

    def chat(self, prompt: str, *, model: Optional[str] = None, system: Optional[str] = None) -> str:
        messages = []
        if system:
            messages.append({"role": "system", "content": system})
        messages.append({"role": "user", "content": prompt})
        body = {"model": model or self.text_model, "messages": messages}
        t0 = time.time()
        resp = requests.post(ENDPOINT, json=body, headers=self._headers(), timeout=self.timeout)
        dt = time.time() - t0
        if resp.status_code != 200:
            raise RuntimeError(f"chat {resp.status_code}: {resp.text[:400]}")
        data = resp.json()
        try:
            text = data["choices"][0]["message"]["content"]
        except (KeyError, IndexError) as e:
            raise RuntimeError(f"chat parse error {e}: {str(data)[:400]}")
        print(f"  [chat {dt:.1f}s, {len(text)} chars]")
        return text

    def image(
        self,
        prompt: str,
        *,
        references: Optional[list[Image.Image]] = None,
        model: Optional[str] = None,
    ) -> Optional[Image.Image]:
        content = []
        for ref in (references or [])[:4]:
            buf = io.BytesIO()
            ref.convert("RGB").save(buf, format="PNG")
            b64 = base64.b64encode(buf.getvalue()).decode("ascii")
            content.append(
                {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}}
            )
        content.append({"type": "text", "text": prompt})

        chosen_model = model or self.image_model
        # FLUX (and other image-only output models) reject ["image","text"];
        # Gemini's image-gen model accepts either. Default to image-only output.
        modalities = ["image"] if chosen_model.startswith("black-forest-labs/") else ["image", "text"]
        body = {
            "model": chosen_model,
            "messages": [{"role": "user", "content": content}],
            "modalities": modalities,
        }
        t0 = time.time()
        resp = requests.post(ENDPOINT, json=body, headers=self._headers(), timeout=self.timeout)
        dt = time.time() - t0
        if resp.status_code != 200:
            print(f"  [image ERR {resp.status_code}: {resp.text[:200]}]")
            return None

        data = resp.json()
        try:
            msg = data["choices"][0]["message"]
        except (KeyError, IndexError):
            print(f"  [image bad shape: {str(data)[:200]}]")
            return None

        img = _extract_image(msg)
        n_refs = len(references or [])
        print(f"  [image {dt:.1f}s, refs={n_refs}, ok={img is not None}]")
        return img


def _extract_image(msg: dict) -> Optional[Image.Image]:
    imgs = msg.get("images")
    if imgs:
        url = imgs[0].get("image_url", {}).get("url")
        return _parse_data_url(url)
    content = msg.get("content")
    if isinstance(content, str):
        return _parse_data_url(content)
    return None


def _parse_data_url(url: Optional[str]) -> Optional[Image.Image]:
    if not url:
        return None
    marker = "base64,"
    idx = url.find(marker)
    if idx < 0:
        return None
    raw = base64.b64decode(url[idx + len(marker):])
    try:
        return Image.open(io.BytesIO(raw)).convert("RGB")
    except Exception as e:
        print(f"  [image decode err: {e}]")
        return None

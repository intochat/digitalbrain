FROM nvidia/cuda:12.4.1-cudnn-runtime-ubuntu22.04@sha256:2fcc4280646484290cc50dce5e65f388dd04352b07cbe89a635703bd1f9aedb6

ARG PERSONAPLEX_COMMIT=3428dfd95309a7f3c84fd93259ded0f810d1ff91
ENV DEBIAN_FRONTEND=noninteractive \
    HF_HOME=/var/cache/huggingface \
    HUGGINGFACE_HUB_CACHE=/var/cache/huggingface/hub \
    PYTHONUNBUFFERED=1

RUN apt-get update \
    && apt-get install --no-install-recommends -y git python3 python3-pip \
    && rm -rf /var/lib/apt/lists/* \
    && git clone https://github.com/NVIDIA/personaplex.git /opt/personaplex \
    && cd /opt/personaplex \
    && git checkout --detach "${PERSONAPLEX_COMMIT}"

WORKDIR /opt/adapter
COPY requirements.lock ./requirements.lock
RUN python3 -m pip install --no-cache-dir --upgrade pip \
    && python3 -m pip install --no-cache-dir -r requirements.lock \
    && python3 -m pip install --no-cache-dir --no-deps /opt/personaplex/moshi

COPY entrypoint.py ./entrypoint.py
VOLUME ["/var/cache/huggingface"]
ENTRYPOINT ["python3", "/opt/adapter/entrypoint.py"]

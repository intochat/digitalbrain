FROM nvidia/cuda:12.4.1-cudnn-runtime-ubuntu22.04@sha256:2fcc4280646484290cc50dce5e65f388dd04352b07cbe89a635703bd1f9aedb6

ARG PERSONAPLEX_COMMIT=3428dfd95309a7f3c84fd93259ded0f810d1ff91
ARG UBUNTU_SNAPSHOT=20250101T000000Z
ENV DEBIAN_FRONTEND=noninteractive \
    HF_HOME=/var/cache/huggingface \
    HUGGINGFACE_HUB_CACHE=/var/cache/huggingface/hub \
    PYTHONUNBUFFERED=1

RUN printf '%s\n' \
        "deb [check-valid-until=no] http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/ jammy main universe" \
        "deb [check-valid-until=no] http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/ jammy-updates main universe" \
        "deb [check-valid-until=no] http://snapshot.ubuntu.com/ubuntu/${UBUNTU_SNAPSHOT}/ jammy-security main universe" \
        > /etc/apt/sources.list \
    && apt-get update \
    && apt-get install --no-install-recommends -y git python3 python3-pip python3-setuptools \
    && rm -rf /var/lib/apt/lists/* \
    && git clone https://github.com/NVIDIA/personaplex.git /opt/personaplex \
    && cd /opt/personaplex \
    && git checkout --detach "${PERSONAPLEX_COMMIT}"

WORKDIR /opt/adapter
COPY pyproject.toml uv.lock requirements.lock build-requirements.lock ./
RUN python3 -m pip install --no-cache-dir --require-hashes -r build-requirements.lock \
    && python3 -m pip install --no-cache-dir --require-hashes -r requirements.lock \
    && python3 -m pip install --no-cache-dir --no-deps --no-build-isolation /opt/personaplex/moshi

COPY entrypoint.py ./entrypoint.py
VOLUME ["/var/cache/huggingface"]
ENTRYPOINT ["python3", "/opt/adapter/entrypoint.py"]

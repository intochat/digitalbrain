FROM nvidia/cuda:13.0.1-cudnn-devel-ubuntu22.04@sha256:56eefb66ca9bb7a8403f7c36debb23e6a83d3e22f745d0571f09f6648f49cacd

ARG PERSONAPLEX_COMMIT=3428dfd95309a7f3c84fd93259ded0f810d1ff91
ENV DEBIAN_FRONTEND=noninteractive \
    HF_HOME=/var/cache/huggingface \
    HUGGINGFACE_HUB_CACHE=/var/cache/huggingface/hub \
    PYTHONUNBUFFERED=1

RUN apt-get update \
    && apt-get install --no-install-recommends -y git libcusparselt0-cuda-13 python3 python3-pip python3-setuptools \
    && printf '%s\n' '/usr/lib/x86_64-linux-gnu/libcusparseLt/13' > /etc/ld.so.conf.d/cusparselt.conf \
    && ldconfig \
    && rm -rf /var/lib/apt/lists/* \
    && git clone https://github.com/NVIDIA/personaplex.git /opt/personaplex \
    && cd /opt/personaplex \
    && git checkout --detach "${PERSONAPLEX_COMMIT}"

WORKDIR /opt/adapter
COPY pyproject.toml uv.lock requirements.lock build-requirements.lock pytorch-blackwell.lock ./
RUN python3 -m pip install --no-cache-dir --require-hashes -r build-requirements.lock \
    && python3 -m pip install --no-cache-dir --no-deps --require-hashes -r pytorch-blackwell.lock \
    && python3 -m pip install --no-cache-dir --no-deps --require-hashes -r requirements.lock \
    && python3 -m pip install --no-cache-dir --no-deps --no-build-isolation /opt/personaplex/moshi

COPY entrypoint.py ./entrypoint.py
VOLUME ["/var/cache/huggingface"]
ENTRYPOINT ["python3", "/opt/adapter/entrypoint.py"]

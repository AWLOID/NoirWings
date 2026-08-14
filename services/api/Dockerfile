FROM python:3.12-slim

# Install .NET 10 runtime + Lua tools needed by NoirWings VM
RUN apt-get update && apt-get install -y --no-install-recommends \
        wget ca-certificates lua5.1 luajit \
    && wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh \
    && apt-get remove -y wget && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /srv

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy all source into /srv/api/ — preserves package structure
COPY . ./api/

# Copy the published NoirWings.Vm engine
COPY noirwings-runtime/ /opt/noirwings/

# Replace Windows .exe with Linux symlinks to system Lua tools
RUN rm -f /opt/noirwings/runtime/luac.exe /opt/noirwings/runtime/luajit.exe /opt/noirwings/runtime/lua51.dll \
    && ln -s /usr/bin/luac5.1 /opt/noirwings/runtime/luac.exe \
    && ln -s /usr/bin/luajit /opt/noirwings/runtime/luajit.exe \
    && touch /opt/noirwings/runtime/lua51.dll

# Create work directory for obfuscation jobs
RUN mkdir -p /tmp/noirwings && chmod 777 /tmp/noirwings

EXPOSE 8000

ENV PORT=8000
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

CMD ["python", "-c", "import os, uvicorn; uvicorn.run('api.main:app', host='0.0.0.0', port=int(os.environ.get('PORT', '8000')))"]

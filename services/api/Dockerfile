FROM mcr.microsoft.com/dotnet/runtime:10.0-preview AS dotnet-base

FROM python:3.12-slim

# Copy .NET runtime from the dotnet base image
COPY --from=dotnet-base /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet

WORKDIR /srv

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy all source into /srv/api/ — preserves package structure
COPY . ./api/

# Copy the published NoirWings.Vm engine
COPY noirwings-runtime/ /opt/noirwings/

# Create work directory for obfuscation jobs
RUN mkdir -p /tmp/noirwings

EXPOSE 8000

ENV PORT=8000

CMD ["python", "-c", "import os, uvicorn; uvicorn.run('api.main:app', host='0.0.0.0', port=int(os.environ.get('PORT', '8000')))"]

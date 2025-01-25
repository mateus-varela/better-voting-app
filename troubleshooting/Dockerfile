FROM ubuntu:20.04

RUN apt-get update && \
        apt-get upgrade && \
        apt install -y \
        net-tools \
        telnet \
        iputils-ping \
        tcpdump \
        unzip \
        wget \
        curl \
        vim \
        dnsutils \
        netcat-openbsd \
        traceroute \
        mysql-client \
        postgresql-client \
        redis-tools \
        redis-server \
        rsync && \
    apt-get clean

CMD ["bash"]
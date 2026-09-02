#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: course.py
# modified: 2019-09-08

class Course(object):

    __slots__ = ['_name','_class_no','_school','_teacher','_status','_href','_ident']

    def __init__(self, name, class_no, school, status=None, href=None, teacher=""):
        self._name = name
        self._class_no = int(class_no) # 确保 01 与 1 为同班号，因为表格软件将 01 视为 1
        self._school = school
        self._teacher = teacher
        self._status = status # (maxi, used) 限选 / 已选
        self._href = href     # 选课链接
        self._ident = (self._name, self._class_no, self._school)

    @property
    def name(self):
        return self._name

    @property
    def class_no(self):
        return self._class_no

    @property
    def school(self):
        return self._school

    @property
    def teacher(self):
        return self._teacher

    @property
    def status(self):
        return self._status

    @property
    def href(self):
        return self._href

    @property
    def max_quota(self):
        if self._status is None:
            raise ValueError("Course quota is unavailable")
        return self._status[0]

    @property
    def used_quota(self):
        if self._status is None:
            raise ValueError("Course quota is unavailable")
        return self._status[1]

    @property
    def remaining_quota(self):
        if self._status is None:
            raise ValueError("Course quota is unavailable")
        maxi, used = self._status
        return maxi - used

    def is_available(self):
        if self._status is None:
            raise ValueError("Course quota is unavailable")
        maxi, used = self._status
        return maxi > used

    def to_simplified(self):
        return Course(self._name, self._class_no, self._school, teacher=self._teacher)

    def __eq__(self, other):
        if not isinstance(other, self.__class__):
            return False
        return self._ident == other._ident

    def __hash__(self):
        return hash(self._ident)

    def __repr__(self):
        if self._status is not None:
            return "%s(%s, %s, %s, %d / %d)" % (
                self.__class__.__name__,
                self._name, self._class_no, self._school, *self._status,
            )
        else:
            return "%s(%s, %s, %s)" % (
                self.__class__.__name__,
                self._name, self._class_no, self._school,
            )
